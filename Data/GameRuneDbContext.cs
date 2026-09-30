using Microsoft.EntityFrameworkCore;

namespace GameListerBackend.Data;

public class GameRuneDbContext(DbContextOptions<GameRuneDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<CachedGame> Games => Set<CachedGame>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<PriceHistoryEntry> PriceHistory => Set<PriceHistoryEntry>();
    public DbSet<PriceAlert> PriceAlerts => Set<PriceAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<AppUser>();
        user.HasKey(u => u.Id);
        user.Property(u => u.Username).HasMaxLength(50).IsRequired();
        user.Property(u => u.Email).HasMaxLength(200).IsRequired();
        user.HasIndex(u => u.Username).IsUnique();
        user.HasIndex(u => u.Email).IsUnique();

        var game = modelBuilder.Entity<CachedGame>();
        game.HasKey(g => g.RawgId);
        game.Property(g => g.Slug).HasMaxLength(200).IsRequired();
        game.Property(g => g.Name).IsRequired();
        game.HasIndex(g => g.Slug).IsUnique();
        game.Property(g => g.RawgRating).HasPrecision(3, 2);

        var favorite = modelBuilder.Entity<Favorite>();
        favorite.HasKey(f => new { f.UserId, f.GameId });
        favorite.HasOne(f => f.User).WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
        favorite.HasOne(f => f.Game).WithMany().HasForeignKey(f => f.GameId).OnDelete(DeleteBehavior.Cascade);
        favorite.ToTable(t => t.HasCheckConstraint("CK_Favorites_Status", "\"Status\" IN ('wishlist','favorite','owned','playing','completed')"));

        var review = modelBuilder.Entity<Review>();
        review.HasKey(r => r.Id);
        review.HasIndex(r => new { r.UserId, r.GameId }).IsUnique();
        review.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        review.HasOne(r => r.Game).WithMany().HasForeignKey(r => r.GameId).OnDelete(DeleteBehavior.Cascade);
        review.ToTable(t => t.HasCheckConstraint("CK_Reviews_Score", "\"Score\" BETWEEN 1 AND 5"));

        var history = modelBuilder.Entity<PriceHistoryEntry>();
        history.HasKey(h => h.Id);
        history.HasOne(h => h.Game).WithMany().HasForeignKey(h => h.GameId).OnDelete(DeleteBehavior.Cascade);
        history.HasIndex(h => new { h.GameId, h.CapturedAt });

        var alert = modelBuilder.Entity<PriceAlert>();
        alert.HasKey(a => a.Id);
        alert.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        alert.HasOne(a => a.Game).WithMany().HasForeignKey(a => a.GameId).OnDelete(DeleteBehavior.Cascade);
    }
}
