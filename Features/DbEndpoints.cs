using System.Security.Claims;
using GameListerBackend.Configuration;
using GameListerBackend.Data;
using GameListerBackend.Models;
using GameListerBackend.Services;
using Microsoft.EntityFrameworkCore;

namespace GameListerBackend.Features;

public static class DbEndpoints
{
    private static readonly HashSet<string> FavoriteStatuses = ["wishlist", "favorite", "owned", "playing", "completed"];

    public static void MapDbEndpoints(WebApplication app)
    {
        var auth = app.MapGroup("/auth");
        auth.MapPost("/register", async (RegisterRequest req, GameRuneDbContext db, IConfiguration config) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            {
                return Results.BadRequest(new { message = "Username, email and password are required." });
            }

            var username = req.Username.Trim();
            var email = req.Email.Trim().ToLowerInvariant();
            if (req.Password.Length < 8)
            {
                return Results.BadRequest(new { message = "Password must be at least 8 characters." });
            }

            if (await db.Users.AnyAsync(u => u.Username == username || u.Email == email))
            {
                return Results.Conflict(new { message = "Username or email is already taken." });
            }

            var user = new AppUser { Username = username, Email = email, PasswordHash = GameRuneAuth.HashPassword(req.Password) };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return Results.Ok(AuthResponse(user, config));
        }).WithName("Register");

        auth.MapPost("/login", async (LoginRequest req, GameRuneDbContext db, IConfiguration config) =>
        {
            if (string.IsNullOrWhiteSpace(req.UsernameOrEmail) || string.IsNullOrWhiteSpace(req.Password))
            {
                return Results.BadRequest(new { message = "Username/email and password are required." });
            }

            var id = req.UsernameOrEmail.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.UsernameOrEmail.Trim() || u.Email == id);
            if (user is null || !GameRuneAuth.VerifyPassword(req.Password, user.PasswordHash))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(AuthResponse(user, config));
        }).WithName("Login");

        app.MapGet("/me", async (ClaimsPrincipal principal, GameRuneDbContext db) =>
        {
            var user = await db.Users.FindAsync(GameRuneAuth.GetUserId(principal));
            return user is null ? Results.NotFound() : Results.Ok(new { user.Id, user.Username, user.Email, user.CreatedAt });
        }).RequireAuthorization().WithName("GetProfile");

        var me = app.MapGroup("/me").RequireAuthorization();
        me.MapGet("/favorites", async (string? status, ClaimsPrincipal principal, GameRuneDbContext db) =>
        {
            var userId = GameRuneAuth.GetUserId(principal);
            var query = db.Favorites.Where(f => f.UserId == userId);
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(f => f.Status == status);
            }

            var items = await query
                .Join(db.Games, f => f.GameId, g => g.RawgId, (f, g) => new { f.Status, f.CreatedAt, Game = g })
                .ToListAsync();
            return Results.Ok(items);
        }).WithName("ListFavorites");

        me.MapPost("/favorites", async (FavoriteRequest req, ClaimsPrincipal principal, GameRuneDbContext db, IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            if (!FavoriteStatuses.Contains(req.Status))
            {
                return Results.BadRequest(new { message = "Invalid status. Use wishlist|favorite|owned|playing|completed." });
            }

            var userId = GameRuneAuth.GetUserId(principal);
            await EnsureGameCachedAsync(req.RawgId, db, httpFactory, config);

            var existing = await db.Favorites.FindAsync(userId, req.RawgId);
            if (existing is null)
            {
                db.Favorites.Add(new Favorite { UserId = userId, GameId = req.RawgId, Status = req.Status });
            }
            else
            {
                existing.Status = req.Status;
            }

            await db.SaveChangesAsync();
            return Results.Ok();
        }).WithName("UpsertFavorite");

        me.MapDelete("/favorites/{rawgId:int}", async (int rawgId, ClaimsPrincipal principal, GameRuneDbContext db) =>
        {
            var existing = await db.Favorites.FindAsync(GameRuneAuth.GetUserId(principal), rawgId);
            if (existing is null)
            {
                return Results.NotFound();
            }

            db.Favorites.Remove(existing);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).WithName("DeleteFavorite");

        me.MapGet("/alerts", async (ClaimsPrincipal principal, GameRuneDbContext db) =>
        {
            var alerts = await db.PriceAlerts.Where(a => a.UserId == GameRuneAuth.GetUserId(principal)).ToListAsync();
            return Results.Ok(alerts);
        }).WithName("ListMyAlerts");

        app.MapGet("/games/{id}/reviews", async (string id, GameRuneDbContext db) =>
        {
            var rawgId = await ResolveRawgIdAsync(id, db);
            if (rawgId is null)
            {
                return Results.NotFound(new { message = "Game was not found." });
            }

            var reviews = await db.Reviews
                .Where(r => r.GameId == rawgId)
                .Join(db.Users, r => r.UserId, u => u.Id, (r, u) => new { r.Id, r.Score, r.Body, r.CreatedAt, Username = u.Username })
                .OrderByDescending(r => r.CreatedAt)
                .Take(100)
                .ToListAsync();
            return Results.Ok(reviews);
        }).WithName("ListReviews");

        app.MapPost("/games/{id}/reviews", async (string id, ReviewRequest req, ClaimsPrincipal principal, GameRuneDbContext db, IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            if (req.Score < 1 || req.Score > 5)
            {
                return Results.BadRequest(new { message = "Score must be between 1 and 5." });
            }

            var rawgId = await ResolveRawgIdAsync(id, db, httpFactory, config);
            if (rawgId is null)
            {
                return Results.NotFound(new { message = "Game was not found." });
            }

            var userId = GameRuneAuth.GetUserId(principal);
            var existing = await db.Reviews.FirstOrDefaultAsync(r => r.UserId == userId && r.GameId == rawgId);
            if (existing is null)
            {
                db.Reviews.Add(new Review { UserId = userId, GameId = rawgId.Value, Score = req.Score, Body = req.Body });
            }
            else
            {
                existing.Score = req.Score;
                existing.Body = req.Body;
            }

            await db.SaveChangesAsync();
            return Results.Ok();
        }).RequireAuthorization().WithName("UpsertReview");

        app.MapGet("/games/{id}/price-history", async (string id, string countryCode, GameRuneDbContext db) =>
        {
            var rawgId = await ResolveRawgIdAsync(id, db);
            if (rawgId is null)
            {
                return Results.NotFound(new { message = "Game was not found." });
            }

            var cc = string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode.Trim().ToUpperInvariant();
            var history = await db.PriceHistory
                .Where(h => h.GameId == rawgId && h.CountryCode == cc)
                .OrderByDescending(h => h.CapturedAt)
                .Take(100)
                .ToListAsync();
            return Results.Ok(history);
        }).WithName("GetPriceHistory");

        app.MapPost("/games/{id}/alerts", async (string id, AlertRequest req, ClaimsPrincipal principal, GameRuneDbContext db, IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            if (req.TargetCents <= 0)
            {
                return Results.BadRequest(new { message = "TargetCents must be positive." });
            }

            var rawgId = await ResolveRawgIdAsync(id, db, httpFactory, config);
            if (rawgId is null)
            {
                return Results.NotFound(new { message = "Game was not found." });
            }

            db.PriceAlerts.Add(new PriceAlert { UserId = GameRuneAuth.GetUserId(principal), GameId = rawgId.Value, TargetCents = req.TargetCents });
            await db.SaveChangesAsync();
            return Results.Created($"/me/alerts", null);
        }).RequireAuthorization().WithName("CreatePriceAlert");

        app.MapPost("/games/{id}/cache", async (string id, GameRuneDbContext db, IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            var game = await EnsureGameCachedAsync(id, db, httpFactory, config);
            return game is null ? Results.NotFound(new { message = "Game was not found." }) : Results.Ok(game);
        }).WithName("CacheGame");
    }

    public static async Task UpsertGameDetailAsync(GameDetailDto detail, string? slug, GameRuneDbContext db)
    {
        var existing = await db.Games.FindAsync(detail.Id);
        if (existing is null)
        {
            db.Games.Add(new CachedGame
            {
                RawgId = detail.Id,
                Slug = slug ?? detail.Id.ToString(),
                Name = detail.Name,
                Description = detail.Description,
                Released = detail.Released,
                RawgRating = detail.Rating,
                CoverUrl = detail.ImageUrl,
                ImageUrls = detail.ImageUrls,
                SteamAppId = detail.SteamAppId,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Name = detail.Name;
            existing.Description = detail.Description;
            existing.Released = detail.Released;
            existing.RawgRating = detail.Rating;
            existing.CoverUrl = detail.ImageUrl;
            existing.ImageUrls = detail.ImageUrls;
            existing.SteamAppId = detail.SteamAppId;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    public static async Task RecordPriceAsync(GameDetailDto detail, string countryCode, GameRuneDbContext db)
    {
        if (detail.SteamAppId is null || detail.SteamPrice is null)
        {
            return;
        }

        var cc = string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode.Trim().ToUpperInvariant();
        db.PriceHistory.Add(new PriceHistoryEntry
        {
            GameId = detail.Id,
            SteamAppId = detail.SteamAppId.Value,
            CountryCode = cc,
            Currency = detail.SteamPrice.Currency ?? cc,
            InitialCents = detail.SteamPrice.Initial,
            FinalCents = detail.SteamPrice.Final,
            DiscountPct = detail.SteamPrice.DiscountPercent,
        });

        var alerts = await db.PriceAlerts
            .Where(a => a.GameId == detail.Id && !a.Triggered && a.TargetCents >= detail.SteamPrice.Final)
            .ToListAsync();
        foreach (var alert in alerts)
        {
            alert.Triggered = true;
        }

        await db.SaveChangesAsync();
    }

    private static object AuthResponse(AppUser user, IConfiguration config) =>
        new { token = GameRuneAuth.GenerateJwt(user, config), user = new { user.Id, user.Username, user.Email } };

    private static async Task<int?> ResolveRawgIdAsync(string id, GameRuneDbContext db, IHttpClientFactory? httpFactory = null, IConfiguration? config = null)
    {
        if (int.TryParse(id, out var numeric))
        {
            return numeric;
        }

        var cached = await db.Games.FirstOrDefaultAsync(g => g.Slug == id);
        if (cached is not null)
        {
            return cached.RawgId;
        }

        if (httpFactory is not null && config is not null)
        {
            var game = await EnsureGameCachedAsync(id, db, httpFactory, config);
            return game?.RawgId;
        }

        return null;
    }

    private static async Task<CachedGame?> EnsureGameCachedAsync(string id, GameRuneDbContext db, IHttpClientFactory httpFactory, IConfiguration config) =>
        int.TryParse(id, out var numeric)
            ? await EnsureGameCachedAsync(numeric, db, httpFactory, config)
            : await EnsureGameCachedBySlugAsync(id, db, httpFactory, config);

    private static async Task<CachedGame?> EnsureGameCachedAsync(int rawgId, GameRuneDbContext db, IHttpClientFactory httpFactory, IConfiguration config)
    {
        var cached = await db.Games.FindAsync(rawgId);
        if (cached is not null)
        {
            return cached;
        }

        var apiKey = ApiKeys.Get(config, "Rawg");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var client = httpFactory.CreateClient("Rawg");
        var response = await client.GetAsync($"games/{rawgId}?key={Uri.EscapeDataString(apiKey)}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var rawg = await response.Content.ReadFromJsonAsync<RawgGameDetail>();
        if (rawg is null)
        {
            return null;
        }

        cached = new CachedGame { RawgId = rawg.Id, Slug = rawg.Slug ?? rawg.Id.ToString(), Name = rawg.Name };
        db.Games.Add(cached);
        await db.SaveChangesAsync();
        return cached;
    }

    private static async Task<CachedGame?> EnsureGameCachedBySlugAsync(string slug, GameRuneDbContext db, IHttpClientFactory httpFactory, IConfiguration config)
    {
        var cached = await db.Games.FirstOrDefaultAsync(g => g.Slug == slug);
        if (cached is not null)
        {
            return cached;
        }

        var apiKey = ApiKeys.Get(config, "Rawg");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var client = httpFactory.CreateClient("Rawg");
        var response = await client.GetAsync($"games/{Uri.EscapeDataString(slug)}?key={Uri.EscapeDataString(apiKey)}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var rawg = await response.Content.ReadFromJsonAsync<RawgGameDetail>();
        if (rawg is null)
        {
            return null;
        }

        cached = new CachedGame { RawgId = rawg.Id, Slug = rawg.Slug ?? slug, Name = rawg.Name };
        db.Games.Add(cached);
        await db.SaveChangesAsync();
        return cached;
    }

    public sealed record RegisterRequest(string Username, string Email, string Password);
    public sealed record LoginRequest(string UsernameOrEmail, string Password);
    public sealed record FavoriteRequest(int RawgId, string Status);
    public sealed record ReviewRequest(short Score, string? Body);
    public sealed record AlertRequest(int TargetCents);
}
