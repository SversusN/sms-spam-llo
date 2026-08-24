using Microsoft.Extensions.Options;
using SmsRecipes.Api.Options;

namespace SmsRecipes.Api.Services;

public interface ISmsGatewayService
{
    Task<SmsGatewayResult> SendAsync(string phone, string text, CancellationToken cancellationToken = default);
    Task<SmsGatewayStatusResult> GetStatusAsync(string messageId, CancellationToken cancellationToken = default);
}

public record SmsGatewayResult
{
    public bool Success { get; init; }
    public string? MessageId { get; init; }
    public string? Error { get; init; }
}

public record SmsGatewayStatusResult
{
    public bool Success { get; init; }
    public string? Status { get; init; }
    public string? Error { get; init; }
    public bool IsFinal { get; init; }
}

public class SmsGatewayService : ISmsGatewayService
{
    private readonly HttpClient _httpClient;
    private readonly SmsGatewayOptions _options;
    private readonly ILogger<SmsGatewayService> _logger;

    public SmsGatewayService(HttpClient httpClient, IOptions<SmsGatewayOptions> options, ILogger<SmsGatewayService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SmsGatewayResult> SendAsync(string phone, string text, CancellationToken cancellationToken = default)
    {
        if (!_options.UseRealGateway)
        {
            _logger.LogInformation("SMS gateway is in stub mode. Phone={Phone}", MaskPhone(phone));
            return new SmsGatewayResult { Success = true, MessageId = Guid.NewGuid().ToString("N"), Error = "Stub mode" };
        }

        if (string.IsNullOrWhiteSpace(_options.Login) || string.IsNullOrWhiteSpace(_options.Password))
        {
            _logger.LogWarning("SMS gateway credentials are not configured");
            return new SmsGatewayResult { Success = false, Error = "SMS gateway credentials not configured" };
        }

        var msisdn = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(msisdn))
        {
            return new SmsGatewayResult { Success = false, Error = "Invalid phone number" };
        }

        if (!IsValidShortcode(_options.Shortcode))
        {
            _logger.LogWarning("SMS gateway shortcode is not valid: {Shortcode}", _options.Shortcode);
            return new SmsGatewayResult { Success = false, Error = $"Shortcode is not valid: {_options.Shortcode}" };
        }

        var query = $"operation=send&login={Uri.EscapeDataString(_options.Login)}" +
                    $"&password={Uri.EscapeDataString(_options.Password)}" +
                    $"&msisdn={Uri.EscapeDataString(msisdn)}";

        if (_options.UseShortcode && !string.IsNullOrWhiteSpace(_options.Shortcode))
        {
            query += $"&shortcode={Uri.EscapeDataString(_options.Shortcode)}";
        }

        query += $"&text={Uri.EscapeDataString(text)}";

        var baseUrl = _options.BaseUrl.TrimEnd('/') + "/";
        var requestUri = $"{baseUrl}?{query}";

        _logger.LogInformation("Sending SMS to {Phone} via gateway", MaskPhone(phone));
        _logger.LogDebug("SMS gateway request: {RequestUri}", MaskPasswordInUrl(requestUri));

        var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var trimmedContent = content.Trim();

        _logger.LogInformation(
            "SMS gateway response for {Phone}: HTTP {StatusCode}, Body={Response}",
            MaskPhone(phone), (int)response.StatusCode, trimmedContent);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("SMS gateway returned HTTP error {StatusCode}: {Response}", (int)response.StatusCode, trimmedContent);
            return new SmsGatewayResult { Success = false, Error = $"HTTP {(int)response.StatusCode}: {trimmedContent}" };
        }

        // Успешный ответ — числовой ID сообщения
        if (long.TryParse(trimmedContent, out _))
        {
            _logger.LogInformation("SMS sent successfully. Phone={Phone}, MessageId={MessageId}", MaskPhone(phone), trimmedContent);
            return new SmsGatewayResult { Success = true, MessageId = trimmedContent };
        }

        // Во всех остальных случаях шлюз вернул ошибку в виде текста
        _logger.LogError("SMS gateway returned error for {Phone}: {Response}", MaskPhone(phone), trimmedContent);
        return new SmsGatewayResult { Success = false, Error = trimmedContent };
    }

    public async Task<SmsGatewayStatusResult> GetStatusAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (!_options.UseRealGateway)
        {
            return new SmsGatewayStatusResult { Success = true, Status = "delivered", IsFinal = true, Error = "Stub mode" };
        }

        if (string.IsNullOrWhiteSpace(_options.Login) || string.IsNullOrWhiteSpace(_options.Password))
        {
            return new SmsGatewayStatusResult { Success = false, Error = "SMS gateway credentials not configured", IsFinal = true };
        }

        if (string.IsNullOrWhiteSpace(messageId))
        {
            return new SmsGatewayStatusResult { Success = false, Error = "MessageId is empty", IsFinal = true };
        }

        var query = $"operation=status&login={Uri.EscapeDataString(_options.Login)}" +
                    $"&password={Uri.EscapeDataString(_options.Password)}" +
                    $"&id={Uri.EscapeDataString(messageId)}";

        var baseUrl = _options.BaseUrl.TrimEnd('/') + "/";
        var requestUri = $"{baseUrl}?{query}";

        _logger.LogInformation("Checking SMS status. MessageId={MessageId}", messageId);
        _logger.LogDebug("SMS status request: {RequestUri}", MaskPasswordInUrl(requestUri));

        var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var trimmedContent = content.Trim();

        _logger.LogInformation(
            "SMS status response for MessageId={MessageId}: HTTP {StatusCode}, Body={Response}",
            messageId, (int)response.StatusCode, trimmedContent);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("SMS status check returned HTTP error {StatusCode}: {Response}", (int)response.StatusCode, trimmedContent);
            return new SmsGatewayStatusResult { Success = false, Error = $"HTTP {(int)response.StatusCode}: {trimmedContent}", IsFinal = false };
        }

        // Известные ошибки шлюза — считаем финальными
        var knownErrors = new[]
        {
            "system error", "login/password is incorrect", "not supported operation",
            "invalid ID", "transaction not found", "mandatory parameter", "invalid parameter",
            "invalid MSISDN", "invalid shortcode", "billing failed"
        };

        if (knownErrors.Any(e => trimmedContent.Contains(e, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogError("SMS status check returned final error for MessageId={MessageId}: {Response}", messageId, trimmedContent);
            return new SmsGatewayStatusResult { Success = false, Error = trimmedContent, IsFinal = true };
        }

        // Известные финальные статусы доставки (документация т2)
        var finalStatuses = new[] { "delivered", "expired", "undeliverable", "rejected" };
        var nonFinalStatuses = new[] { "enrote", "accepted", "billed", "unknown", "not_sent" };

        if (finalStatuses.Contains(trimmedContent, StringComparer.OrdinalIgnoreCase))
        {
            return new SmsGatewayStatusResult { Success = true, Status = trimmedContent, IsFinal = true };
        }

        if (nonFinalStatuses.Contains(trimmedContent, StringComparer.OrdinalIgnoreCase))
        {
            return new SmsGatewayStatusResult { Success = true, Status = trimmedContent, IsFinal = false };
        }

        // Неизвестный ответ — пробуем ещё раз позже
        return new SmsGatewayStatusResult { Success = false, Error = trimmedContent, IsFinal = false };
    }

    private static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return string.Empty;

        // Заменяем ведущую 8 на 7
        if (digits.Length == 11 && digits.StartsWith('8'))
            digits = "7" + digits.Substring(1);

        // Российские номера: 7 + 10 цифр
        if (digits.Length == 11 && digits.StartsWith('7'))
            return digits;

        // 10 цифр без кода страны
        if (digits.Length == 10)
            return "7" + digits;

        return digits;
    }

    private static string MaskPhone(string phone)
    {
        if (phone.Length <= 4) return phone;
        return phone.Substring(0, 2) + "***" + phone.Substring(phone.Length - 2);
    }

    private static string MaskPasswordInUrl(string url)
    {
        // Заменяем password=<value> на password=*** в целях безопасности логов
        if (string.IsNullOrWhiteSpace(url))
            return url;

        try
        {
            var queryStart = url.IndexOf('?');
            if (queryStart < 0)
                return url;

            var basePart = url.Substring(0, queryStart + 1);
            var query = url.Substring(queryStart + 1);
            var pairs = query.Split('&');
            var masked = new List<string>(pairs.Length);

            foreach (var pair in pairs)
            {
                if (pair.StartsWith("password=", StringComparison.OrdinalIgnoreCase))
                {
                    masked.Add("password=***");
                }
                else
                {
                    masked.Add(pair);
                }
            }

            return basePart + string.Join("&", masked);
        }
        catch
        {
            return url;
        }
    }

    private static bool IsValidShortcode(string? shortcode)
    {
        if (string.IsNullOrWhiteSpace(shortcode))
            return false;

        if (shortcode.Length > 11)
            return false;

        // Допустимы только ASCII-символы
        if (shortcode.Any(c => c > 127))
            return false;

        // Запрещённые символы согласно документации т2: \x00-\x1F, [, \, ], ^, `, {, |, }, ~
        var forbiddenChars = new HashSet<char>("[\\]^`{|}~");
        if (shortcode.Any(c => c < 0x20 || forbiddenChars.Contains(c)))
            return false;

        return true;
    }
}
