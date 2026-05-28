namespace GameListerBackend.Models;

public class GameDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }
}
