namespace SmsRecipes.Api.Models;

public record Consent
{
    public Consent() { }

    public int Id { get; init; }
    public Guid UserGuid { get; init; }
    public string PatientSnils { get; init; } = string.Empty;
    public string PatientName { get; init; } = string.Empty;
    public DateTime? BirthDate { get; init; }
    public string? Phone { get; init; }
    public bool IsConsentGiven { get; init; }
    public string? ConsentType { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? RevokedAt { get; init; }
}
