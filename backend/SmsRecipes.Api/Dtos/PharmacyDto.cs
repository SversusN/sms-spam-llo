namespace SmsRecipes.Api.Dtos;

public record PharmacyDto
{
    public Guid Guid { get; init; }
    public string Name { get; init; } = string.Empty;
}
