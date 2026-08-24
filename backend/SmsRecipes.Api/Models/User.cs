namespace SmsRecipes.Api.Models;

public record User
{
    public User() { }

    public int Id { get; init; }
    public Guid Guid { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Note { get; init; }
    public string? Email { get; init; }
    public string Password { get; init; } = string.Empty;
    public bool IsSystem { get; init; }
    public DateTime Date { get; init; }
    public bool? Deleted { get; init; }
    public int? Attempt { get; init; }
    public bool? Blocked { get; init; }
    public DateTime? Authenticated { get; init; }
}
