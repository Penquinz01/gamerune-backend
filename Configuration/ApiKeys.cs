namespace GameListerBackend.Configuration;

public static class ApiKeys
{
    public static string? Get(IConfiguration configuration, string providerName)
    {
        var environmentKey = $"{providerName.ToUpperInvariant()}_API_KEY";
        var environmentValue = Environment.GetEnvironmentVariable(environmentKey);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        var configurationValue = configuration[$"{providerName}:ApiKey"];
        return string.IsNullOrWhiteSpace(configurationValue) ? null : configurationValue;
    }
}
