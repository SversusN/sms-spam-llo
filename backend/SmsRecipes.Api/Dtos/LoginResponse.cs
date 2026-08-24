namespace SmsRecipes.Api.Dtos;

public record LoginResponse(string Token, string UserName, Guid UserGuid);
