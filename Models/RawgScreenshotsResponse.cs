namespace GameListerBackend.Models;

public class RawgScreenshotsResponse
{
    public RawgScreenshot[] Results { get; set; } = [];
}

public class RawgScreenshot
{
    public string Image { get; set; } = string.Empty;
}
