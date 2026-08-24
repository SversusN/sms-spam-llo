namespace SmsRecipes.Api.Dtos;

public record ConsentDto
{
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

public record CreateConsentRequest
{
    public string PatientSnils { get; init; } = string.Empty;
    public string PatientName { get; init; } = string.Empty;
    public DateTime? BirthDate { get; init; }
    public string? Phone { get; init; }
    public bool IsConsentGiven { get; init; }
    public string? ConsentType { get; init; }
}

public record ConsentFilterRequest
{
    public string? PatientSnils { get; init; }
    public string? PatientName { get; init; }
    public bool? IsConsentGiven { get; init; }
    public bool? IsActive { get; init; }
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
    public string? SortDirection { get; init; } = "DESC";
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public record PatientLookupResult
{
    public string PatientSnils { get; init; } = string.Empty;
    public string PatientName { get; init; } = string.Empty;
    public DateTime? BirthDate { get; init; }
    public string? Phone { get; init; }
}
