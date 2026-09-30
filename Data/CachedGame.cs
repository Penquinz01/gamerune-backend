namespace GameListerBackend.Data;

public class CachedGame
{
    public int RawgId { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateOnly? Released { get; set; }

    public decimal? RawgRating { get; set; }

    public string? CoverUrl { get; set; }

    public string[] ImageUrls { get; set; } = [];

    public int? SteamAppId { get; set; }

    public string? RawgPayload { get; set; }

    public DateTime CachedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
