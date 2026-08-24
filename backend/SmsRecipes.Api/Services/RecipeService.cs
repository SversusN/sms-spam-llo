using Dapper;
using SmsRecipes.Api.Data;
using SmsRecipes.Api.Dtos;

namespace SmsRecipes.Api.Services;

public interface IRecipeService
{
    Task<PagedResult<RecipeDto>> GetRecipesAsync(Guid userGuid, RecipeFilterRequest filter);
    Task<List<RecipeDto>> ExportRecipesAsync(Guid userGuid, RecipeFilterRequest filter);
    Task<List<PharmacyDto>> GetUserPharmaciesAsync(Guid userGuid);
}

public class RecipeService : IRecipeService
{
    private readonly IDbConnectionFactory _dbFactory;
    private readonly IConsentService _consentService;

    public RecipeService(IDbConnectionFactory dbFactory, IConsentService consentService)
    {
        _dbFactory = dbFactory;
        _consentService = consentService;
    }

    private static readonly string[] StatesToExclude = { "SAVE", "DEL", "ANNULLED", "INVALID", "PATIENT_DECLINE" };

    private static string BuildSqlBase()
    {
        return @"
SELECT  
    RecipeId = R.[ID],  
    ApName = AP.[NAME],  
    LsName = X.[LS_NAME],
    IncomeDate = R2C.[INCOME_DATE],
    DateNumberRecipe = X.[DATE_NUMBER_RECIPE],
    DateIssueEnd = X.[DATE_ISSUE_END],
    ExpirationDate = R.[EXPIRATION_DATE],
    PatientName = X.[PATIENT_NAME],
    PatientPhone = IND.[SMS_PHONE],
    SaleDate = R2C.[SALE_DATE],
    SmsDate = COALESCE(RR.[SMS_DATE], (
        SELECT MAX(SL.[CreatedAt])
        FROM [SmsRecipesApp].[dbo].[SmsLog] SL (NOLOCK)
        WHERE SL.[RecipeId] = R.[ID] AND SL.[UserGuid] = @UserGuid
    )),
    IndividualSnils = IND.INDIVIDUAL_SNILS,
    Program = PRG.[PROGRAM_SHORT_NAME],
    Dosage = RI.DOSAGE,
    Quantity = X2.UNIFY_NAME
FROM [RECIPE] R WITH (NOLOCK)
INNER JOIN [BLANK] B (NOLOCK) ON B.[GUID] = R.[BLANK_GUID]  
INNER JOIN [RECIPE_NUMBER] RN (NOLOCK) ON RN.[GUID] = B.[RECIPE_NUMBER_GUID]  
INNER JOIN [RECIPE_ITEM] RI (NOLOCK) ON RI.[RECIPE_GUID] = R.[GUID] AND RI.[DELETED] IS NULL  
INNER JOIN [RECIPE_2_CONTRACTOR] R2C (NOLOCK) ON R2C.[RECIPE_GUID] = R.[GUID]  
INNER JOIN [CONTRACTOR] AS AP (NOLOCK) ON AP.[GUID] = R2C.[CONTRACTOR_GUID]  
INNER JOIN [INDIVIDUAL] IND (NOLOCK) ON IND.[GUID] = R.[INDIVIDUAL_GUID]  
INNER JOIN [DOCTOR_2_CONTRACTOR] D2C (NOLOCK) ON D2C.[GUID] = B.[DOCTOR_2_CONTRACTOR_GUID]  
INNER JOIN CUREFORM CR (NOLOCK) ON CR.GUID = RI.CUREFORM_GUID  
INNER JOIN PROGRAM PRG (NOLOCK) ON PRG.GUID = B.PROGRAM_GUID  
LEFT JOIN [PRODUCT] AS PROD (NOLOCK) ON PROD.[GUID] = RI.[PRODUCT_GUID]  
LEFT JOIN [MNN] (NOLOCK) ON [MNN].[GUID] = RI.[MNN_GUID]  
LEFT JOIN [TRN] (NOLOCK) ON [TRN].[GUID] = RI.[TRN_GUID]  
INNER JOIN [VALID_PERIOD] VP ON VP.[GUID] = R.[VALID_PERIOD_GUID] AND VP.[IS_LGOTA] = 1
LEFT JOIN [RECIPE_RESERVED] RR (NOLOCK) ON RR.[RECIPE_GUID] = R.[GUID]  
CROSS APPLY  
(  
 SELECT  
  [LS_NAME] = COALESCE(PROD.[NAME], [MNN].[NAME], [TRN].[NAME]),  
  [DATE_NUMBER_RECIPE] = CONVERT(VARCHAR, R.[ISSUE_DATE], 104) + ' №' + ISNULL(RN.[NUMBER],''),  
  [PATIENT_NAME] = ISNULL(IND.[INDIVIDUAL_LAST_NAME] + ' ', '') + ISNULL(IND.[INDIVIDUAL_FIRST_NAME] + ' ', '') + ISNULL(IND.[INDIVIDUAL_MIDDLE_NAME], ''),  
  [DATE_ISSUE_END] = CASE SUBSTRING(VP.[VALUE], LEN(VP.[VALUE]), 1)  
   WHEN 'd' THEN DATEADD(DAY, CAST(SUBSTRING(VP.[VALUE], 1, LEN(VP.[VALUE]) - 1) AS INT), R.[ISSUE_DATE])  
   WHEN 'm' THEN DATEADD(MONTH, CAST(SUBSTRING(VP.[VALUE], 1, LEN(VP.[VALUE]) - 1) AS INT), R.[ISSUE_DATE])  
  END  
) X  
CROSS APPLY  
(  
SELECT  
  [UNIFY_NAME] = cast(CASE WHEN RI.[QUANTITY_ATOM] = 0 THEN RI.[QUANTITY_DOSAGE] ELSE RI.[QUANTITY_ATOM] END as varchar(255))+' '+  
     (select top 1 case when me1.STANDARD_INN like '%таблетк%'
         then ' шт'
         else me1.name_unit
         end
     from mnn mnn2
     join product p1 on mnn2.guid = p1.MNN_GUID
     join PRODUCT_ESKLP pe1 on p1.guid = pe1.UNIFY_GUID
     join MZV_ESKLP me1 on pe1.CODE = me1.KLP_CODE
     where  mnn2.guid = mnn.guid and p1.CUREFORM_GUID = cr.guid
     and pe1.DELETED is NULL and p1.DELETED is null )  
       
)X2  
WHERE  
 R2C.[IS_DEFERRED] = 1   
 AND R.[DOCUMENT_STATE_ID] NOT IN (
     SELECT [ID] FROM [DOCUMENT_STATE] WHERE [CODE] IN @StateCodes
 )
 AND IND.SMS_PHONE IS NOT NULL
 AND R2C.[CONTRACTOR_GUID] IN @ContractorGuids
";
    }

    private async Task<(string Sql, DynamicParameters Parameters, List<int> SentRecipeIds)> BuildQueryAsync(Guid userGuid, RecipeFilterRequest filter)
    {
        using var connection = _dbFactory.CreateEfsConnection();
        await connection.OpenAsync();

        var contractorGuids = (await connection.QueryAsync<Guid>(
            @"SELECT r2c.CONTRACTOR_GUID 
              FROM [USER_2_ROLE] u2r
              INNER JOIN [ROLE] r ON r.GUID = u2r.ROLE_GUID
              INNER JOIN [ROLE_2_CONTRACTOR] r2c ON r2c.ROLE_GUID = u2r.ROLE_GUID
              WHERE u2r.USER_GUID = @UserGuid",
            new { UserGuid = userGuid })).ToList();

        List<int> sentRecipeIds = new();
        using (var appConn = _dbFactory.CreateAppConnection())
        {
            await appConn.OpenAsync();
            sentRecipeIds = (await appConn.QueryAsync<int>(
                @"SELECT DISTINCT [RecipeId] FROM [SmsQueue] WHERE [UserGuid] = @UserGuid AND [Status] = 'Sent'
                  UNION
                  SELECT DISTINCT [RecipeId] FROM [SmsLog] WHERE [UserGuid] = @UserGuid AND [Status] = 'Sent'",
                new { UserGuid = userGuid })).ToList();
        }

        var sqlBase = BuildSqlBase();
        var parameters = new DynamicParameters();
        parameters.Add("StateCodes", StatesToExclude);
        parameters.Add("ContractorGuids", contractorGuids);
        parameters.Add("UserGuid", userGuid);

        if (filter.OnlyNotSent == true && sentRecipeIds.Any())
        {
            sqlBase += " AND R.[ID] NOT IN @SentRecipeIds";
            parameters.Add("SentRecipeIds", sentRecipeIds);
        }

        if (filter.DateFrom.HasValue)
        {
            sqlBase += " AND R2C.[INCOME_DATE] >= @DateFrom";
            parameters.Add("DateFrom", filter.DateFrom.Value);
        }
        if (filter.DateTo.HasValue)
        {
            sqlBase += " AND R2C.[INCOME_DATE] < @DateTo";
            parameters.Add("DateTo", filter.DateTo.Value.AddDays(1));
        }
        if (!string.IsNullOrWhiteSpace(filter.PatientName))
        {
            sqlBase += " AND X.[PATIENT_NAME] LIKE @PatientName";
            parameters.Add("PatientName", $"%{filter.PatientName}%");
        }
        if (!string.IsNullOrWhiteSpace(filter.PatientPhone))
        {
            sqlBase += " AND IND.[SMS_PHONE] LIKE @PatientPhone";
            parameters.Add("PatientPhone", $"%{filter.PatientPhone}%");
        }
        if (!string.IsNullOrWhiteSpace(filter.LsName))
        {
            sqlBase += " AND X.[LS_NAME] LIKE @LsName";
            parameters.Add("LsName", $"%{filter.LsName}%");
        }
        if (!string.IsNullOrWhiteSpace(filter.IndividualSnils))
        {
            sqlBase += " AND IND.[INDIVIDUAL_SNILS] LIKE @IndividualSnils";
            parameters.Add("IndividualSnils", $"%{filter.IndividualSnils}%");
        }
        if (filter.OnlyDeferred == true)
        {
            sqlBase += " AND R.[DOCUMENT_STATE_ID] IN (12, 34)";
        }

        if (filter.ContractorGuid.HasValue)
        {
            contractorGuids = contractorGuids.Contains(filter.ContractorGuid.Value)
                ? new List<Guid> { filter.ContractorGuid.Value }
                : new List<Guid>();
        }
        parameters.Add("ContractorGuids", contractorGuids);

        return (sqlBase, parameters, sentRecipeIds);
    }

    public async Task<PagedResult<RecipeDto>> GetRecipesAsync(Guid userGuid, RecipeFilterRequest filter)
    {
        var (sqlBase, parameters, _) = await BuildQueryAsync(userGuid, filter);

        using var connection = _dbFactory.CreateEfsConnection();
        await connection.OpenAsync();

        var countSql = $"SELECT COUNT(*) FROM ({sqlBase}) AS T";
        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);

        var orderDirection = filter.SortDirection?.ToUpper() == "ASC" ? "ASC" : "DESC";
        var pagedSql = $@"
{sqlBase}
ORDER BY [INCOME_DATE] {orderDirection}
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        parameters.Add("Offset", (filter.Page - 1) * filter.PageSize);
        parameters.Add("PageSize", filter.PageSize);

        var recipes = (await connection.QueryAsync<RecipeDto>(pagedSql, parameters)).ToList();
        await EnrichStatusesAsync(userGuid, recipes);
        await EnrichConsentAsync(recipes);

        return new PagedResult<RecipeDto>(recipes, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<List<RecipeDto>> ExportRecipesAsync(Guid userGuid, RecipeFilterRequest filter)
    {
        var (sqlBase, parameters, _) = await BuildQueryAsync(userGuid, filter);

        using var connection = _dbFactory.CreateEfsConnection();
        await connection.OpenAsync();

        var orderDirection = filter.SortDirection?.ToUpper() == "ASC" ? "ASC" : "DESC";
        var exportSql = $@"
{sqlBase}
ORDER BY [INCOME_DATE] {orderDirection}";

        var recipes = (await connection.QueryAsync<RecipeDto>(exportSql, parameters)).ToList();
        await EnrichStatusesAsync(userGuid, recipes);
        await EnrichConsentAsync(recipes);

        return recipes;
    }

    private async Task EnrichStatusesAsync(Guid userGuid, List<RecipeDto> recipes)
    {
        if (!recipes.Any()) return;

        var recipeIds = recipes.Select(r => r.RecipeId).ToList();
        using var appConn = _dbFactory.CreateAppConnection();
        await appConn.OpenAsync();

        var queueStatuses = await appConn.QueryAsync<(int RecipeId, string Status)>(
            @"SELECT [RecipeId], [Status] FROM (
                  SELECT [RecipeId], [Status],
                         ROW_NUMBER() OVER (PARTITION BY [RecipeId] ORDER BY [CreatedAt] DESC, [Id] DESC) AS rn
                  FROM [SmsQueue]
                  WHERE [UserGuid] = @UserGuid AND [RecipeId] IN @RecipeIds
              ) t WHERE rn = 1",
            new { UserGuid = userGuid, RecipeIds = recipeIds });

        var logStatuses = await appConn.QueryAsync<(int RecipeId, string Status, string? DeliveryStatus)>(
            @"SELECT [RecipeId], [Status], [DeliveryStatus] FROM (
                  SELECT [RecipeId], [Status], [DeliveryStatus],
                         ROW_NUMBER() OVER (PARTITION BY [RecipeId] ORDER BY [CreatedAt] DESC, [Id] DESC) AS rn
                  FROM [SmsLog]
                  WHERE [UserGuid] = @UserGuid AND [RecipeId] IN @RecipeIds
              ) t WHERE rn = 1",
            new { UserGuid = userGuid, RecipeIds = recipeIds });

        var statusMap = queueStatuses.ToDictionary(x => x.RecipeId, x => x.Status);
        var deliveryStatusMap = new Dictionary<int, string?>();
        foreach (var log in logStatuses)
        {
            if (!statusMap.ContainsKey(log.RecipeId) || log.Status == "Sent")
                statusMap[log.RecipeId] = log.Status;
            deliveryStatusMap[log.RecipeId] = log.DeliveryStatus;
        }

        for (int i = 0; i < recipes.Count; i++)
        {
            var recipeId = recipes[i].RecipeId;
            var updates = new List<System.Action>();
            if (statusMap.TryGetValue(recipeId, out var status))
                recipes[i] = recipes[i] with { SmsStatus = status };
            if (deliveryStatusMap.TryGetValue(recipeId, out var deliveryStatus))
                recipes[i] = recipes[i] with { SmsDeliveryStatus = deliveryStatus };
        }
    }

    private async Task EnrichConsentAsync(List<RecipeDto> recipes)
    {
        if (!recipes.Any()) return;

        var snilsList = recipes.Select(r => r.IndividualSnils).ToList();
        var activeSnils = await _consentService.GetActiveMailingSnilsAsync(snilsList);

        for (int i = 0; i < recipes.Count; i++)
        {
            var snils = recipes[i].IndividualSnils;
            var hasConsent = !string.IsNullOrWhiteSpace(snils)
                && activeSnils.Contains(new string(snils.Where(char.IsDigit).ToArray()));
            recipes[i] = recipes[i] with { HasMailingConsent = hasConsent };
        }
    }

    public async Task<List<PharmacyDto>> GetUserPharmaciesAsync(Guid userGuid)
    {
        using var connection = _dbFactory.CreateEfsConnection();
        await connection.OpenAsync();

        var pharmacies = await connection.QueryAsync<PharmacyDto>(
            @"SELECT DISTINCT c.[GUID] AS Guid, c.[FULLNAME] AS Name
              FROM [USER_2_ROLE] u2r
              INNER JOIN [ROLE] r ON r.[GUID] = u2r.[ROLE_GUID]
              INNER JOIN [ROLE_2_CONTRACTOR] r2c ON r2c.[ROLE_GUID] = u2r.[ROLE_GUID]
              INNER JOIN [CONTRACTOR] c ON c.[GUID] = r2c.[CONTRACTOR_GUID]
              WHERE u2r.[USER_GUID] = @UserGuid
              ORDER BY c.[FULLNAME]",
            new { UserGuid = userGuid });

        return pharmacies.ToList();
    }
}
