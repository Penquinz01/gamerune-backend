using System.Text.Json.Serialization;

namespace GameListerBackend.Models;

public class RawgGamesResponse
{
    public int Count { get; set; }

    public string? Next { get; set; }

    public string? Previous { get; set; }

    public RawgGame[] Results { get; set; } = [];
}

public class RawgGame
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("background_image")]
    public string? BackgroundImage { get; set; }

    public string? Description { get; set; }
}
