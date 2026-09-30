namespace GameListerBackend.Data;

public class PriceHistoryEntry
{
    public long Id { get; set; }

    public int GameId { get; set; }

    public int SteamAppId { get; set; }

    public string CountryCode { get; set; } = "US";

    public string Currency { get; set; } = string.Empty;

    public int InitialCents { get; set; }

    public int FinalCents { get; set; }

    public int DiscountPct { get; set; }

    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    public CachedGame? Game { get; set; }
}
