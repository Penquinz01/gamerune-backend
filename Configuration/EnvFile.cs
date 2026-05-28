namespace GameListerBackend.Configuration;

public static class EnvFile
{
    public static void Load(string fileName = ".env")
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), fileName);
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmedLine.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmedLine[..separatorIndex].Trim();
            var value = trimmedLine[(separatorIndex + 1)..].Trim().Trim('"');

            if (string.IsNullOrWhiteSpace(key) || Environment.GetEnvironmentVariable(key) is not null)
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
