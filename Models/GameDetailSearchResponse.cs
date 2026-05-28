namespace GameListerBackend.Models;

public class GameDetailSearchResponse
{
    public int Count { get; set; }

    public string? Next { get; set; }

    public string? Previous { get; set; }

    public GameDetailDto[] Results { get; set; } = [];
}
