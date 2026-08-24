namespace SmsRecipes.Api.Models;

public record SmsQueueItem
{
    public SmsQueueItem() { }

    public int Id { get; init; }
    public int RecipeId { get; init; }
    public Guid UserGuid { get; init; }
    public string? IndividualSnils { get; init; }
    public DateTime CreatedAt { get; init; }
    public string Status { get; init; } = "Pending";
    public string? ErrorMessage { get; init; }
    public DateTime? ProcessedAt { get; init; }
}
