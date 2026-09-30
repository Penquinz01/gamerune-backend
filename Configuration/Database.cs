namespace GameListerBackend.Configuration;

public static class Database
{
    public static string? GetConnectionString(IConfiguration configuration)
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            try
            {
                var uri = new Uri(databaseUrl);
                var userInfo = uri.UserInfo.Split(':', 2);
                var builder = new Npgsql.NpgsqlConnectionStringBuilder
                {
                    Host = uri.Host,
                    Port = uri.Port > 0 ? uri.Port : 5432,
                    Username = Uri.UnescapeDataString(userInfo[0]),
                    Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
                    Database = uri.AbsolutePath.Trim('/'),
                    SslMode = Npgsql.SslMode.Require,
                };
                return builder.ConnectionString;
            }
            catch
            {
                return databaseUrl;
            }
        }

        return configuration.GetConnectionString("Default");
    }
}
