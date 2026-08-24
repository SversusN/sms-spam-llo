using Dapper;
using Microsoft.Extensions.Options;
using SmsRecipes.Api.Data;
using SmsRecipes.Api.Options;

namespace SmsRecipes.Api.Services;

public class SmsBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SmsBackgroundService> _logger;

    public SmsBackgroundService(IServiceProvider serviceProvider, ILogger<SmsBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SMS Background Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueueAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SMS queue.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var queueService = scope.ServiceProvider.GetRequiredService<ISmsQueueService>();
        var gatewayService = scope.ServiceProvider.GetRequiredService<ISmsGatewayService>();
        var consentService = scope.ServiceProvider.GetRequiredService<IConsentService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var requireMailingConsent = scope.ServiceProvider.GetRequiredService<IOptions<FeatureOptions>>().Value.RequireMailingConsent;
        var templateOptions = scope.ServiceProvider.GetRequiredService<IOptions<SmsTemplateOptions>>().Value;

        var items = await queueService.GetPendingItemsAsync(100);
        if (!items.Any()) return;

        using var efsConnection = dbFactory.CreateEfsConnection();
        await efsConnection.OpenAsync(stoppingToken);

        using var appConnection = dbFactory.CreateAppConnection();
        await appConnection.OpenAsync(stoppingToken);

        foreach (var item in items)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                var recipe = await efsConnection.QueryFirstOrDefaultAsync<dynamic>(
                    @"SELECT 
                        R.[ID] as RecipeId,
                        IND.[SMS_PHONE] as Phone,
                        IND.[INDIVIDUAL_SNILS] as IndividualSnils,
                        IND.[INDIVIDUAL_FIRST_NAME] as FirstName,
                        IND.[INDIVIDUAL_MIDDLE_NAME] as MiddleName,
                        AP.[NAME] as ApName,
                        AP.[PHONE] as ApPhone,
                        RN.[NUMBER] as RecipeNumber,
                        LS_NAME = COALESCE(PROD.[NAME], [MNN].[NAME], [TRN].[NAME])
                      FROM [RECIPE] R
                      INNER JOIN [INDIVIDUAL] IND ON IND.[GUID] = R.[INDIVIDUAL_GUID]
                      INNER JOIN [RECIPE_2_CONTRACTOR] R2C ON R2C.[RECIPE_GUID] = R.[GUID]
                      INNER JOIN [CONTRACTOR] AP ON AP.[GUID] = R2C.[CONTRACTOR_GUID]
                      INNER JOIN [BLANK] B ON B.[GUID] = R.[BLANK_GUID]
                      INNER JOIN [RECIPE_NUMBER] RN ON RN.[GUID] = B.[RECIPE_NUMBER_GUID]
                      INNER JOIN [RECIPE_ITEM] RI ON RI.[RECIPE_GUID] = R.[GUID] AND RI.[DELETED] IS NULL
                      LEFT JOIN [PRODUCT] AS PROD ON PROD.[GUID] = RI.[PRODUCT_GUID]
                      LEFT JOIN [MNN] ON [MNN].[GUID] = RI.[MNN_GUID]
                      LEFT JOIN [TRN] ON [TRN].[GUID] = RI.[TRN_GUID]
                      WHERE R.[ID] = @RecipeId",
                    new { item.RecipeId });

                if (recipe == null)
                {
                    _logger.LogWarning($"Recipe {item.RecipeId} not found in EFS");
                    await queueService.MarkAsProcessedAsync(item.Id, "Failed", "Recipe not found in EFS");
                    continue;
                }

                var individualSnils = recipe.IndividualSnils?.ToString() ?? item.IndividualSnils ?? "";

                // Повторная проверка согласия перед отправкой (могло быть отозвано после постановки в очередь)
                if (requireMailingConsent && !await consentService.HasActiveMailingConsentAsync(individualSnils))
                {
                    _logger.LogWarning($"No active mailing consent for RecipeId={item.RecipeId}, Snils={individualSnils}");
                    await queueService.MarkAsProcessedAsync(item.Id, "Failed", "Отсутствует активное согласие на рассылку");
                    continue;
                }

                var phone = recipe.Phone?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(phone))
                {
                    _logger.LogWarning($"Phone number is empty for RecipeId={item.RecipeId}");
                    await queueService.MarkAsProcessedAsync(item.Id, "Failed", "Phone number is empty");
                    continue;
                }

                var firstName = recipe.FirstName?.ToString() ?? "";
                var middleName = recipe.MiddleName?.ToString() ?? "";
                var namePart = $"{firstName} {middleName}".Trim();
                if (string.IsNullOrWhiteSpace(namePart))
                {
                    namePart = "Пациент";
                }

                var apName = recipe.ApName?.ToString() ?? "";
                var apPhone = recipe.ApPhone?.ToString() ?? "";
                var recipeNumber = MaskRecipeNumber(recipe.RecipeNumber?.ToString() ?? "");
                var lsName = recipe.LS_NAME?.ToString() ?? "";
                var message = templateOptions.Text
                    .Replace("{Name}", namePart, StringComparison.OrdinalIgnoreCase)
                    .Replace("{DrugName}", lsName, StringComparison.OrdinalIgnoreCase)
                    .Replace("{PharmacyName}", apName, StringComparison.OrdinalIgnoreCase)
                    .Replace("{PharmacyPhone}", apPhone, StringComparison.OrdinalIgnoreCase)
                    .Replace("{RecipeNumber}", recipeNumber, StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation($"Sending SMS for RecipeId={item.RecipeId}");
                var sendResult = await gatewayService.SendAsync(phone, message, stoppingToken);

                if (!sendResult.Success)
                {
                    await appConnection.ExecuteAsync(
                        @"INSERT INTO [SmsLog] ([RecipeId], [UserGuid], [IndividualSnils], [Phone], [Message], [Status], [ProviderResponse], [DeliveryStatus], [CreatedAt])
                          VALUES (@RecipeId, @UserGuid, @IndividualSnils, @Phone, @Message, @Status, @ProviderResponse, @DeliveryStatus, @CreatedAt)",
                        new
                        {
                            item.RecipeId,
                            item.UserGuid,
                            IndividualSnils = individualSnils,
                            Phone = phone,
                            Message = message,
                            Status = "Failed",
                            ProviderResponse = sendResult.Error,
                            DeliveryStatus = (string?)null,
                            CreatedAt = DateTime.Now
                        });

                    await queueService.MarkAsProcessedAsync(item.Id, "Failed", sendResult.Error);
                    continue;
                }

                // Сохраняем успешную отправку
                var logId = await appConnection.QuerySingleAsync<int>(
                    @"INSERT INTO [SmsLog] ([RecipeId], [UserGuid], [IndividualSnils], [Phone], [Message], [Status], [ProviderResponse], [DeliveryStatus], [CreatedAt])
                      VALUES (@RecipeId, @UserGuid, @IndividualSnils, @Phone, @Message, @Status, @ProviderResponse, @DeliveryStatus, @CreatedAt);
                      SELECT CAST(SCOPE_IDENTITY() as int);",
                    new
                    {
                        item.RecipeId,
                        item.UserGuid,
                        IndividualSnils = individualSnils,
                        Phone = phone,
                        Message = message,
                        Status = "Sent",
                        ProviderResponse = sendResult.MessageId,
                        DeliveryStatus = (string?)null,
                        CreatedAt = DateTime.Now
                    });

                // Проверяем финальный статус доставки (3 попытки с интервалом 5 секунд)
                string? deliveryStatus = null;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

                    var statusResult = await gatewayService.GetStatusAsync(sendResult.MessageId ?? "", stoppingToken);
                    if (!statusResult.Success)
                    {
                        _logger.LogInformation($"Status check attempt {attempt} for RecipeId={item.RecipeId} failed: {statusResult.Error}");
                        if (statusResult.IsFinal)
                        {
                            deliveryStatus = statusResult.Error;
                            break;
                        }
                        continue;
                    }

                    deliveryStatus = statusResult.Status;
                    _logger.LogInformation($"Status check attempt {attempt} for RecipeId={item.RecipeId}: {deliveryStatus}");

                    if (statusResult.IsFinal)
                        break;
                }

                // Обновляем запись лога финальным статусом доставки
                if (!string.IsNullOrWhiteSpace(deliveryStatus))
                {
                    await appConnection.ExecuteAsync(
                        "UPDATE [SmsLog] SET [DeliveryStatus] = @DeliveryStatus WHERE [Id] = @Id",
                        new { Id = logId, DeliveryStatus = deliveryStatus });
                }

                await queueService.MarkAsProcessedAsync(item.Id, "Sent");
                _logger.LogInformation($"SMS for RecipeId={item.RecipeId} processed. DeliveryStatus={deliveryStatus ?? "unknown"}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to process queue item {item.Id}");
                await queueService.MarkAsProcessedAsync(item.Id, "Failed", ex.Message);
            }
        }
    }

    private static string MaskRecipeNumber(string recipeNumber)
    {
        if (string.IsNullOrWhiteSpace(recipeNumber))
            return string.Empty;

        var digits = new string(recipeNumber.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return recipeNumber;

        var visible = digits.Length >= 5 ? digits[^5..] : digits;
        return "*" + visible;
    }
}
