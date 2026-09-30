namespace GameListerBackend.Data;

public class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public int GameId { get; set; }

    public short Score { get; set; }

    public string? Body { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }

    public CachedGame? Game { get; set; }
}
