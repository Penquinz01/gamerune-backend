namespace GameListerBackend.Data;

public class Favorite
{
    public Guid UserId { get; set; }

    public int GameId { get; set; }

    public string Status { get; set; } = "wishlist";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }

    public CachedGame? Game { get; set; }
}
