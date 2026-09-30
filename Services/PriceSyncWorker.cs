using GameListerBackend.Data;
using GameListerBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace GameListerBackend.Services;

public class PriceSyncWorker(IServiceProvider services, IConfiguration configuration, ILogger<PriceSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("PriceSync:Enabled", true))
        {
            logger.LogInformation("PriceSync disabled.");
            return;
        }

        var intervalHours = configuration.GetValue("PriceSync:IntervalHours", 6);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(intervalHours, 1)));

        await SyncOnceAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SyncOnceAsync(stoppingToken);
        }
    }

    private async Task SyncOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GameRuneDbContext>();
            var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            var steamClient = httpFactory.CreateClient("Steam");

            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                return;
            }

            var games = await db.Games.Where(g => g.SteamAppId != null).ToListAsync(cancellationToken);
            foreach (var game in games)
            {
                var countryCodes = await db.PriceHistory
                    .Where(h => h.GameId == game.RawgId)
                    .Select(h => h.CountryCode)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                if (countryCodes.Count == 0)
                {
                    countryCodes.Add("US");
                }

                foreach (var cc in countryCodes)
                {
                    var price = await FetchSteamPriceAsync(steamClient, game.SteamAppId!.Value, cc, cancellationToken);
                    if (price is null)
                    {
                        continue;
                    }

                    db.PriceHistory.Add(new PriceHistoryEntry
                    {
                        GameId = game.RawgId,
                        SteamAppId = game.SteamAppId!.Value,
                        CountryCode = cc,
                        Currency = price.Currency ?? cc,
                        InitialCents = price.Initial,
                        FinalCents = price.Final,
                        DiscountPct = price.DiscountPercent,
                    });

                    var alerts = await db.PriceAlerts
                        .Where(a => a.GameId == game.RawgId && !a.Triggered && a.TargetCents >= price.Final)
                        .ToListAsync(cancellationToken);
                    foreach (var alert in alerts)
                    {
                        alert.Triggered = true;
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("PriceSync completed for {Count} games.", games.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PriceSync failed.");
        }
    }

    private static async Task<SteamPriceDto?> FetchSteamPriceAsync(HttpClient steamClient, int steamAppId, string countryCode, CancellationToken cancellationToken)
    {
        try
        {
            var response = await steamClient.GetAsync(
                $"appdetails?appids={steamAppId}&cc={Uri.EscapeDataString(countryCode)}&filters=basic,price_overview",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var details = await response.Content.ReadFromJsonAsync<Dictionary<string, SteamAppDetailsResponse>>(cancellationToken);
            if (details is null || !details.TryGetValue(steamAppId.ToString(), out var app) || !app.Success || app.Data is null)
            {
                return null;
            }

            return app.Data.PriceOverview;
        }
        catch
        {
            return null;
        }
    }
}
