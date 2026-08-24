using Microsoft.Data.SqlClient;

namespace SmsRecipes.Api.Data;

public interface IDbConnectionFactory
{
    SqlConnection CreateEfsConnection();
    SqlConnection CreateAppConnection();
}

public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _efsConnectionString;
    private readonly string _appConnectionString;

    public DbConnectionFactory(IConfiguration configuration)
    {
        _efsConnectionString = configuration.GetConnectionString("EfsDatabase")!;
        _appConnectionString = configuration.GetConnectionString("AppDatabase")!;
    }

    public SqlConnection CreateEfsConnection() => new SqlConnection(_efsConnectionString);
    public SqlConnection CreateAppConnection() => new SqlConnection(_appConnectionString);
}
