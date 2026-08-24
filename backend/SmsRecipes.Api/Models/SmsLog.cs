namespace SmsRecipes.Api.Models;

public record SmsLog
{
    public SmsLog() { }

    public int Id { get; init; }
    public int RecipeId { get; init; }
    public Guid UserGuid { get; init; }
    public string? IndividualSnils { get; init; }
    public string Phone { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? ProviderResponse { get; init; }
    public string? DeliveryStatus { get; init; }
    public DateTime CreatedAt { get; init; }
}
