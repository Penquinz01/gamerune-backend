using System.Text.Json.Serialization;

namespace GameListerBackend.Models;

public class RawgGameStoresResponse
{
    public RawgGameStore[] Results { get; set; } = [];
}

public class RawgGameStore
{
    [JsonPropertyName("store_id")]
    public int StoreId { get; set; }

    public string? Url { get; set; }

    public RawgStore? Store { get; set; }
}

public class RawgStore
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;
}
