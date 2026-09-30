namespace GameListerBackend.Models;

public class GameDetailSearchResponse
{
    public int Count { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalPages { get; set; }

    public bool HasNext { get; set; }

    public bool HasPrevious { get; set; }

    public string? Next { get; set; }

    public string? Previous { get; set; }

    public GameDetailDto[] Results { get; set; } = [];
}
