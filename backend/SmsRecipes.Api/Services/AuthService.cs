using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using SmsRecipes.Api.Data;
using SmsRecipes.Api.Dtos;
using SmsRecipes.Api.Models;
using Dapper;
using System.Security.Cryptography;

namespace SmsRecipes.Api.Services;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);
    Task<List<UserListItem>> GetUsersAsync();
}

public class AuthService : IAuthService
{
    private readonly IDbConnectionFactory _dbFactory;
    private readonly IConfiguration _configuration;

    public AuthService(IDbConnectionFactory dbFactory, IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _configuration = configuration;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        using var connection = _dbFactory.CreateEfsConnection();
        await connection.OpenAsync();

        var user = await connection.QueryFirstOrDefaultAsync<User>(
            @"SELECT [ID] as Id, [GUID] as Guid, [CODE] as Code, [NAME] as Name, [PASSWORD] as Password, [IS_SYSTEM] as IsSystem, [DELETED] as Deleted, [BLOCKED] as Blocked
              FROM [USER]
              WHERE [CODE] = @Login AND ([DELETED] IS NULL OR [DELETED] = 0) AND ([BLOCKED] IS NULL OR [BLOCKED] = 0)",
            new { Login = request.Login });

        if (user == null)
            return null;

        var passwordHash = GetHashBase64(request.Password);
        if (user.Password != passwordHash)
            return null;

        var token = GenerateJwtToken(user);

        return new LoginResponse(token, user.Name, user.Guid);
    }

    public async Task<List<UserListItem>> GetUsersAsync()
    {
        using var connection = _dbFactory.CreateEfsConnection();
        await connection.OpenAsync();

        var users = await connection.QueryAsync<UserListItem>(
            @"SELECT [GUID] as Guid, [CODE] as Code, [NAME] as Name
              FROM [USER]
              WHERE ([DELETED] IS NULL OR [DELETED] = 0) AND ([BLOCKED] IS NULL OR [BLOCKED] = 0)
              ORDER BY [NAME]");

        return users.ToList();
    }

    private static string GetHashBase64(string value)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(value);
        bytes = MD5.HashData(bytes);
        return Convert.ToBase64String(bytes);
    }

    private string GenerateJwtToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Code),
            new Claim("UserGuid", user.Guid.ToString()),
            new Claim(ClaimTypes.GivenName, user.Name)
        };

        var expires = DateTime.Now.AddHours(double.Parse(_configuration["Jwt:ExpireHours"]!));

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
