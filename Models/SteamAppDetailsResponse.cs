using System.Text.Json.Serialization;

namespace GameListerBackend.Models;

public class SteamAppDetailsResponse
{
    public bool Success { get; set; }

    public SteamAppDetails? Data { get; set; }
}

public class SteamAppDetails
{
    [JsonPropertyName("is_free")]
    public bool IsFree { get; set; }

    [JsonPropertyName("price_overview")]
    public SteamPriceDto? PriceOverview { get; set; }
}

public class SteamPriceDto
{
    public string Currency { get; set; } = string.Empty;

    public int Initial { get; set; }

    public int Final { get; set; }

    [JsonPropertyName("discount_percent")]
    public int DiscountPercent { get; set; }

    [JsonPropertyName("initial_formatted")]
    public string? InitialFormatted { get; set; }

    [JsonPropertyName("final_formatted")]
    public string? FinalFormatted { get; set; }
}
