namespace GameListerBackend.Models;

public class GamesResponse
{
    public int Count { get; set; }

    public string? Next { get; set; }

    public string? Previous { get; set; }

    public GameDto[] Results { get; set; } = [];
}
