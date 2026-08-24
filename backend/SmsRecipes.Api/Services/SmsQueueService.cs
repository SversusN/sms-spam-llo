using Dapper;
using Microsoft.Extensions.Options;
using SmsRecipes.Api.Data;
using SmsRecipes.Api.Models;
using SmsRecipes.Api.Options;

namespace SmsRecipes.Api.Services;

public interface ISmsQueueService
{
    Task<int> EnqueueAsync(Guid userGuid, List<int> recipeIds);
    Task<List<SmsQueueItem>> GetPendingItemsAsync(int limit = 100);
    Task MarkAsProcessedAsync(int queueItemId, string status, string? errorMessage = null);
    Task<List<SmsLog>> GetLogsAsync(Guid userGuid, int page, int pageSize, string? status, string? individualSnils, int? recipeId, DateTime? dateFrom, DateTime? dateTo);
    Task<List<SmsQueueItem>> GetQueueAsync(Guid userGuid, int page, int pageSize, string? status, string? individualSnils, int? recipeId, DateTime? dateFrom, DateTime? dateTo);
}

public class SmsQueueService : ISmsQueueService
{
    private readonly IDbConnectionFactory _dbFactory;
    private readonly IConsentService _consentService;
    private readonly bool _requireMailingConsent;

    public SmsQueueService(IDbConnectionFactory dbFactory, IConsentService consentService, IOptions<FeatureOptions> featureOptions)
    {
        _dbFactory = dbFactory;
        _consentService = consentService;
        _requireMailingConsent = featureOptions.Value.RequireMailingConsent;
    }

    public async Task<int> EnqueueAsync(Guid userGuid, List<int> recipeIds)
    {
        if (!recipeIds.Any()) return 0;

        using var efsConnection = _dbFactory.CreateEfsConnection();
        await efsConnection.OpenAsync();

        // Получаем СНИЛС для рецептов
        var snilsMap = (await efsConnection.QueryAsync<(int RecipeId, string? IndividualSnils)>(
            @"SELECT R.[ID] as RecipeId, IND.[INDIVIDUAL_SNILS] as IndividualSnils
              FROM [RECIPE] R
              INNER JOIN [INDIVIDUAL] IND ON IND.[GUID] = R.[INDIVIDUAL_GUID]
              WHERE R.[ID] IN @RecipeIds",
            new { RecipeIds = recipeIds })).ToDictionary(x => x.RecipeId, x => x.IndividualSnils);

        // Оставляем только тех, у кого есть активное согласие на рассылку (если требуется)
        HashSet<int> consentedRecipeIds;
        if (_requireMailingConsent)
        {
            var activeSnils = await _consentService.GetActiveMailingSnilsAsync(snilsMap.Values);
            consentedRecipeIds = snilsMap
                .Where(x => !string.IsNullOrWhiteSpace(x.Value) && activeSnils.Contains(NormalizeSnils(x.Value)))
                .Select(x => x.Key)
                .ToHashSet();
        }
        else
        {
            consentedRecipeIds = snilsMap.Keys.ToHashSet();
        }

        using var connection = _dbFactory.CreateAppConnection();
        await connection.OpenAsync();

        // Блокируем рецепты, которые уже отправлены или в очереди
        var blockedRecipeIds = (await connection.QueryAsync<int>(
            @"SELECT DISTINCT [RecipeId] FROM [SmsQueue] WHERE [RecipeId] IN @RecipeIds AND [Status] IN ('Pending', 'Sent', 'StubSent')
              UNION
              SELECT DISTINCT [RecipeId] FROM [SmsLog] WHERE [RecipeId] IN @RecipeIds AND [Status] IN ('Sent', 'StubSent')",
            new { RecipeIds = recipeIds })).ToHashSet();

        var allowedIds = recipeIds
            .Distinct()
            .Where(id => !blockedRecipeIds.Contains(id) && consentedRecipeIds.Contains(id))
            .ToList();

        const string insertSql = @"
INSERT INTO [SmsQueue] ([RecipeId], [UserGuid], [IndividualSnils], [CreatedAt], [Status])
VALUES (@RecipeId, @UserGuid, @IndividualSnils, @CreatedAt, 'Pending')";

        var inserted = 0;
        foreach (var recipeId in allowedIds)
        {
            await connection.ExecuteAsync(insertSql, new
            {
                RecipeId = recipeId,
                UserGuid = userGuid,
                IndividualSnils = snilsMap.GetValueOrDefault(recipeId),
                CreatedAt = DateTime.Now
            });
            inserted++;
        }

        return inserted;
    }

    public async Task<List<SmsQueueItem>> GetPendingItemsAsync(int limit = 100)
    {
        using var connection = _dbFactory.CreateAppConnection();
        await connection.OpenAsync();

        var items = await connection.QueryAsync<SmsQueueItem>(
            @"SELECT TOP (@Limit) [Id], [RecipeId], [UserGuid], [IndividualSnils], [CreatedAt], [Status], [ErrorMessage], [ProcessedAt]
              FROM [SmsQueue]
              WHERE [Status] = 'Pending'
              ORDER BY [CreatedAt]",
            new { Limit = limit });

        return items.ToList();
    }

    public async Task MarkAsProcessedAsync(int queueItemId, string status, string? errorMessage = null)
    {
        using var connection = _dbFactory.CreateAppConnection();
        await connection.OpenAsync();

        await connection.ExecuteAsync(
            @"UPDATE [SmsQueue] 
              SET [Status] = @Status, [ErrorMessage] = @ErrorMessage, [ProcessedAt] = @ProcessedAt
              WHERE [Id] = @Id",
            new { Id = queueItemId, Status = status, ErrorMessage = errorMessage, ProcessedAt = DateTime.Now });
    }

    public async Task<List<SmsLog>> GetLogsAsync(Guid userGuid, int page, int pageSize, string? status, string? individualSnils, int? recipeId, DateTime? dateFrom, DateTime? dateTo)
    {
        using var connection = _dbFactory.CreateAppConnection();
        await connection.OpenAsync();

        var whereClauses = new List<string> { "[UserGuid] = @UserGuid" };
        var parameters = new DynamicParameters();
        parameters.Add("UserGuid", userGuid);

        if (!string.IsNullOrWhiteSpace(status))
        {
            whereClauses.Add("[Status] = @Status");
            parameters.Add("Status", status);
        }
        if (!string.IsNullOrWhiteSpace(individualSnils))
        {
            whereClauses.Add("[IndividualSnils] LIKE @IndividualSnils");
            parameters.Add("IndividualSnils", $"%{individualSnils}%");
        }
        if (recipeId.HasValue)
        {
            whereClauses.Add("[RecipeId] = @RecipeId");
            parameters.Add("RecipeId", recipeId.Value);
        }
        if (dateFrom.HasValue)
        {
            whereClauses.Add("[CreatedAt] >= @DateFrom");
            parameters.Add("DateFrom", dateFrom.Value);
        }
        if (dateTo.HasValue)
        {
            whereClauses.Add("[CreatedAt] < @DateTo");
            parameters.Add("DateTo", dateTo.Value.AddDays(1));
        }

        var whereSql = string.Join(" AND ", whereClauses);

        var sql = $@"
SELECT [Id], [RecipeId], [UserGuid], [IndividualSnils], [Phone], [Message], [Status], [ProviderResponse], [DeliveryStatus], [CreatedAt]
FROM [SmsLog]
WHERE {whereSql}
ORDER BY [CreatedAt] DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        parameters.Add("Offset", (page - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var logs = await connection.QueryAsync<SmsLog>(sql, parameters);
        return logs.ToList();
    }

    public async Task<List<SmsQueueItem>> GetQueueAsync(Guid userGuid, int page, int pageSize, string? status, string? individualSnils, int? recipeId, DateTime? dateFrom, DateTime? dateTo)
    {
        using var connection = _dbFactory.CreateAppConnection();
        await connection.OpenAsync();

        var whereClauses = new List<string> { "[UserGuid] = @UserGuid" };
        var parameters = new DynamicParameters();
        parameters.Add("UserGuid", userGuid);

        if (!string.IsNullOrWhiteSpace(status))
        {
            whereClauses.Add("[Status] = @Status");
            parameters.Add("Status", status);
        }
        if (!string.IsNullOrWhiteSpace(individualSnils))
        {
            whereClauses.Add("[IndividualSnils] LIKE @IndividualSnils");
            parameters.Add("IndividualSnils", $"%{individualSnils}%");
        }
        if (recipeId.HasValue)
        {
            whereClauses.Add("[RecipeId] = @RecipeId");
            parameters.Add("RecipeId", recipeId.Value);
        }
        if (dateFrom.HasValue)
        {
            whereClauses.Add("[CreatedAt] >= @DateFrom");
            parameters.Add("DateFrom", dateFrom.Value);
        }
        if (dateTo.HasValue)
        {
            whereClauses.Add("[CreatedAt] < @DateTo");
            parameters.Add("DateTo", dateTo.Value.AddDays(1));
        }

        var whereSql = string.Join(" AND ", whereClauses);

        var sql = $@"
SELECT [Id], [RecipeId], [UserGuid], [IndividualSnils], [CreatedAt], [Status], [ErrorMessage], [ProcessedAt]
FROM [SmsQueue]
WHERE {whereSql}
ORDER BY [CreatedAt] DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        parameters.Add("Offset", (page - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var items = await connection.QueryAsync<SmsQueueItem>(sql, parameters);
        return items.ToList();
    }

    private static string NormalizeSnils(string? snils)
    {
        if (string.IsNullOrWhiteSpace(snils))
            return string.Empty;
        return new string(snils.Where(char.IsDigit).ToArray());
    }
}
