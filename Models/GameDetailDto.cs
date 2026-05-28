namespace GameListerBackend.Models;

public class GameDetailDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    public string[] ImageUrls { get; set; } = [];

    public string? Description { get; set; }

    public DateOnly? Released { get; set; }

    public decimal? Rating { get; set; }

    public int? SteamAppId { get; set; }

    public SteamPriceDto? SteamPrice { get; set; }
}
