namespace SmsRecipes.Api.Dtos;

public record RecipeFilterRequest(
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    string? PatientName = null,
    string? PatientPhone = null,
    string? LsName = null,
    string? IndividualSnils = null,
    Guid? ContractorGuid = null,
    bool? OnlyDeferred = true,
    bool? OnlyNotSent = true,
    string? SortColumn = "IncomeDate",
    string? SortDirection = "DESC",
    int Page = 1,
    int PageSize = 50
);
