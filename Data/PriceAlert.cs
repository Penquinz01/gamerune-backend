namespace GameListerBackend.Data;

public class PriceAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public int GameId { get; set; }

    public int TargetCents { get; set; }

    public bool Triggered { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }

    public CachedGame? Game { get; set; }
}
