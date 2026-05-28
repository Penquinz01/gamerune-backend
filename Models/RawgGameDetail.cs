using System.Text.Json.Serialization;

namespace GameListerBackend.Models;

public class RawgGameDetail
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("background_image")]
    public string? BackgroundImage { get; set; }

    public string? Description { get; set; }

    [JsonPropertyName("description_raw")]
    public string? DescriptionRaw { get; set; }

    public DateOnly? Released { get; set; }

    public decimal? Rating { get; set; }
}
