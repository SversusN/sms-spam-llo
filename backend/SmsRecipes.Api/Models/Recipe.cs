namespace SmsRecipes.Api.Models;

public record Recipe
{
    public Recipe() { }

    public int RecipeId { get; init; }
    public string ApName { get; init; } = string.Empty;
    public string LsName { get; init; } = string.Empty;
    public DateTime? IncomeDate { get; init; }
    public string? DateNumberRecipe { get; init; }
    public DateTime? DateIssueEnd { get; init; }
    public DateTime? ExpirationDate { get; init; }
    public string? PatientName { get; init; }
    public string? PatientPhone { get; init; }
    public DateTime? SaleDate { get; init; }
    public DateTime? SmsDate { get; init; }
    public string? IndividualSnils { get; init; }
    public string? Program { get; init; }
    public string? Dosage { get; init; }
    public string? Quantity { get; init; }
}
