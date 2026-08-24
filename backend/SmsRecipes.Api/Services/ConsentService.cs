using Dapper;
using SmsRecipes.Api.Data;
using SmsRecipes.Api.Dtos;

namespace SmsRecipes.Api.Services;

public interface IConsentService
{
    Task<PagedResult<ConsentDto>> GetConsentsAsync(Guid userGuid, ConsentFilterRequest filter);
    Task<ConsentDto?> GetConsentByIdAsync(Guid userGuid, int id);
    Task<int> CreateConsentAsync(Guid userGuid, CreateConsentRequest request);
    Task<bool> RevokeConsentAsync(Guid userGuid, int id);
    Task<PatientLookupResult?> LookupPatientBySnilsAsync(string snils);
    Task<bool> HasActiveMailingConsentAsync(string snils);
    Task<HashSet<string>> GetActiveMailingSnilsAsync(IEnumerable<string?> snilsList);
}

public class ConsentService : IConsentService
{
    private readonly IDbConnectionFactory _dbFactory;

    public ConsentService(IDbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<PagedResult<ConsentDto>> GetConsentsAsync(Guid userGuid, ConsentFilterRequest filter)
    {
        using var connection = _dbFactory.CreateAppConnection();
        await connection.OpenAsync();

        var parameters = new DynamicParameters();
        parameters.Add("UserGuid", userGuid);

        var whereClauses = new List<string> { "[UserGuid] = @UserGuid" };

        if (!string.IsNullOrWhiteSpace(filter.PatientSnils))
        {
            whereClauses.Add("[PatientSnils] LIKE @PatientSnils");
            parameters.Add("PatientSnils", $"%{filter.PatientSnils.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(filter.PatientName))
        {
            whereClauses.Add("[PatientName] LIKE @PatientName");
            parameters.Add("PatientName", $"%{filter.PatientName.Trim()}%");
        }

        if (filter.IsConsentGiven.HasValue)
        {
            whereClauses.Add("[IsConsentGiven] = @IsConsentGiven");
            parameters.Add("IsConsentGiven", filter.IsConsentGiven.Value);
        }

        if (filter.IsActive.HasValue)
        {
            whereClauses.Add(filter.IsActive.Value
                ? "[IsConsentGiven] = 1 AND [RevokedAt] IS NULL"
                : "([IsConsentGiven] = 0 OR [RevokedAt] IS NOT NULL)");
        }

        if (filter.DateFrom.HasValue)
        {
            whereClauses.Add("[CreatedAt] >= @DateFrom");
            parameters.Add("DateFrom", filter.DateFrom.Value);
        }

        if (filter.DateTo.HasValue)
        {
            whereClauses.Add("[CreatedAt] < @DateTo");
            parameters.Add("DateTo", filter.DateTo.Value.AddDays(1));
        }

        var whereSql = string.Join(" AND ", whereClauses);
        var orderDirection = filter.SortDirection?.ToUpper() == "ASC" ? "ASC" : "DESC";

        var countSql = $"SELECT COUNT(*) FROM [dbo].[Consent] WHERE {whereSql}";
        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);

        var pagedSql = $@"
SELECT
    [Id],
    [UserGuid],
    [PatientSnils],
    [PatientName],
    [BirthDate],
    [Phone],
    [IsConsentGiven],
    [ConsentType],
    [CreatedAt],
    [RevokedAt]
FROM [dbo].[Consent]
WHERE {whereSql}
ORDER BY [CreatedAt] {orderDirection}
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        parameters.Add("Offset", (filter.Page - 1) * filter.PageSize);
        parameters.Add("PageSize", filter.PageSize);

        var items = (await connection.QueryAsync<ConsentDto>(pagedSql, parameters)).ToList();
        return new PagedResult<ConsentDto>(items, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<ConsentDto?> GetConsentByIdAsync(Guid userGuid, int id)
    {
        using var connection = _dbFactory.CreateAppConnection();
        await connection.OpenAsync();

        var consent = await connection.QueryFirstOrDefaultAsync<ConsentDto>(
            "SELECT [Id], [UserGuid], [PatientSnils], [PatientName], [BirthDate], [Phone], [IsConsentGiven], [ConsentType], [CreatedAt], [RevokedAt] " +
            "FROM [dbo].[Consent] WHERE [Id] = @Id AND [UserGuid] = @UserGuid",
            new { Id = id, UserGuid = userGuid });

        return consent;
    }

    public async Task<int> CreateConsentAsync(Guid userGuid, CreateConsentRequest request)
    {
        using var connection = _dbFactory.CreateAppConnection();
        await connection.OpenAsync();

        var parameters = new DynamicParameters();
        parameters.Add("UserGuid", userGuid);
        parameters.Add("PatientSnils", NormalizeSnils(request.PatientSnils));
        parameters.Add("PatientName", request.PatientName.Trim());
        parameters.Add("BirthDate", request.BirthDate);
        parameters.Add("Phone", request.Phone);
        parameters.Add("IsConsentGiven", request.IsConsentGiven);
        parameters.Add("ConsentType", request.ConsentType);

        var id = await connection.ExecuteScalarAsync<int>(
            "INSERT INTO [dbo].[Consent] ([UserGuid], [PatientSnils], [PatientName], [BirthDate], [Phone], [IsConsentGiven], [ConsentType], [CreatedAt]) " +
            "OUTPUT INSERTED.[Id] " +
            "VALUES (@UserGuid, @PatientSnils, @PatientName, @BirthDate, @Phone, @IsConsentGiven, @ConsentType, GETDATE())",
            parameters);

        return id;
    }

    public async Task<bool> RevokeConsentAsync(Guid userGuid, int id)
    {
        using var connection = _dbFactory.CreateAppConnection();
        await connection.OpenAsync();

        var affected = await connection.ExecuteAsync(
            "UPDATE [dbo].[Consent] SET [RevokedAt] = GETDATE() WHERE [Id] = @Id AND [UserGuid] = @UserGuid AND [RevokedAt] IS NULL",
            new { Id = id, UserGuid = userGuid });

        return affected > 0;
    }

    public async Task<PatientLookupResult?> LookupPatientBySnilsAsync(string snils)
    {
        var normalizedSnils = NormalizeSnils(snils);
        if (string.IsNullOrWhiteSpace(normalizedSnils))
            return null;

        using var connection = _dbFactory.CreateEfsConnection();
        await connection.OpenAsync();

        var result = await connection.QueryFirstOrDefaultAsync<PatientLookupResult>(
            "SELECT TOP 1 " +
            "  [INDIVIDUAL_SNILS] AS PatientSnils, " +
            "  ISNULL([INDIVIDUAL_LAST_NAME] + ' ', '') + ISNULL([INDIVIDUAL_FIRST_NAME] + ' ', '') + ISNULL([INDIVIDUAL_MIDDLE_NAME], '') AS PatientName, " +
            "  [SMS_PHONE] AS Phone " +
            "FROM [INDIVIDUAL] WITH (NOLOCK) " +
            "WHERE REPLACE(REPLACE([INDIVIDUAL_SNILS], '-', ''), ' ', '') = @Snils " +
            "   OR [INDIVIDUAL_SNILS] = @Snils",
            new { Snils = normalizedSnils });

        return result;
    }

    public async Task<bool> HasActiveMailingConsentAsync(string snils)
    {
        var normalized = NormalizeSnils(snils);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var set = await GetActiveMailingSnilsAsync(new[] { normalized });
        return set.Contains(normalized);
    }

    public async Task<HashSet<string>> GetActiveMailingSnilsAsync(IEnumerable<string?> snilsList)
    {
        var normalized = snilsList
            .Select(NormalizeSnils)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();

        if (!normalized.Any())
            return new HashSet<string>();

        using var connection = _dbFactory.CreateAppConnection();
        await connection.OpenAsync();

        var result = await connection.QueryAsync<string>(
            "SELECT DISTINCT [PatientSnils] FROM [dbo].[Consent] " +
            "WHERE [PatientSnils] IN @Snils " +
            "  AND [IsConsentGiven] = 1 " +
            "  AND [RevokedAt] IS NULL",
            new { Snils = normalized });

        return result.ToHashSet();
    }

    private static string NormalizeSnils(string? snils)
    {
        if (string.IsNullOrWhiteSpace(snils))
            return string.Empty;

        return new string(snils.Where(char.IsDigit).ToArray());
    }
}
