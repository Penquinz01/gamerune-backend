using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using GameListerBackend.Configuration;
using GameListerBackend.Data;
using GameListerBackend.Features;
using GameListerBackend.Models;
using GameListerBackend.Services;

EnvFile.Load();

var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:3000", "http://localhost:5173", "http://localhost:4200", "https://gamerune.vercel.app", "https://gamerune.janbaas.me/"];

        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("GameApi", limiterOptions =>
    {
        limiterOptions.PermitLimit = builder.Configuration.GetValue("RateLimiting:GameApi:PermitLimit", 60);
        limiterOptions.Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:GameApi:WindowSeconds", 60));
        limiterOptions.QueueLimit = builder.Configuration.GetValue("RateLimiting:GameApi:QueueLimit", 0);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});
builder.Services.AddHttpClient("Rawg", client =>
{
    client.BaseAddress = new Uri("https://api.rawg.io/api/");
});
builder.Services.AddHttpClient("Steam", client =>
{
    client.BaseAddress = new Uri("https://store.steampowered.com/api/");
});

var connectionString = Database.GetConnectionString(builder.Configuration);
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<GameRuneDbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddHostedService<PriceSyncWorker>();
}

var jwtKey = builder.Configuration["Jwt:Key"] ?? Environment.GetEnvironmentVariable("JWT_KEY");
if (!string.IsNullOrWhiteSpace(jwtKey))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "gamerune",
                ValidAudience = builder.Configuration["Jwt:Audience"] ?? "gamerune-frontend",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            };
        });
    builder.Services.AddAuthorization();
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseRateLimiter();

if (!string.IsNullOrWhiteSpace(jwtKey))
{
    app.UseAuthentication();
    app.UseAuthorization();
}

if (!string.IsNullOrWhiteSpace(connectionString))
{
    try
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<GameRuneDbContext>().Database.Migrate();
        DbEndpoints.MapDbEndpoints(app);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Database migration failed; DB endpoints disabled.");
    }
}

app.MapGet("/games", async (
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken,
    int page = 1,
    int pageSize = 20,
    string? search = null) =>
{
    var apiKey = ApiKeys.Get(configuration, "Rawg");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.Problem("RAWG API key is not configured.", statusCode: StatusCodes.Status500InternalServerError);
    }

    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, 40);

    var client = httpClientFactory.CreateClient("Rawg");
    var requestUri = $"games?key={Uri.EscapeDataString(apiKey)}&page={page}&page_size={pageSize}";
    if (!string.IsNullOrWhiteSpace(search))
    {
        requestUri += $"&search={Uri.EscapeDataString(search)}";
    }

    var response = await client.GetAsync(requestUri, cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
        return Results.Problem("RAWG request failed.", statusCode: StatusCodes.Status502BadGateway);
    }

    var rawgResponse = await response.Content.ReadFromJsonAsync<RawgGamesResponse>(cancellationToken);
    var games = rawgResponse?.Results
        .Select(game => new GameDto
        {
            Id = game.Id,
            Name = game.Name,
            ImageUrl = game.BackgroundImage
        })
        .ToArray() ?? [];

    return Results.Ok(new GamesResponse
    {
        Count = rawgResponse?.Count ?? 0,
        Page = page,
        PageSize = pageSize,
        TotalPages = GetTotalPages(rawgResponse?.Count ?? 0, pageSize),
        HasNext = HasNextPage(rawgResponse?.Count ?? 0, page, pageSize),
        HasPrevious = page > 1,
        Next = HasNextPage(rawgResponse?.Count ?? 0, page, pageSize)
            ? BuildGamesPageLink(page + 1, pageSize, search)
            : null,
        Previous = page > 1
            ? BuildGamesPageLink(page - 1, pageSize, search)
            : null,
        Results = games
    });
})
.WithName("GetGames")
.RequireRateLimiting("GameApi");

app.MapGet("/games/search/details", async (
    string query,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken,
    int page = 1,
    int pageSize = 5,
    string countryCode = "US") =>
{
    if (string.IsNullOrWhiteSpace(query))
    {
        return Results.BadRequest(new { message = "Search query is required." });
    }

    var rawgApiKey = ApiKeys.Get(configuration, "Rawg");
    if (string.IsNullOrWhiteSpace(rawgApiKey))
    {
        return Results.Problem("RAWG API key is not configured.", statusCode: StatusCodes.Status500InternalServerError);
    }

    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, 10);

    var rawgClient = httpClientFactory.CreateClient("Rawg");
    var steamClient = httpClientFactory.CreateClient("Steam");
    var searchResponse = await rawgClient.GetAsync(
        $"games?key={Uri.EscapeDataString(rawgApiKey)}&search={Uri.EscapeDataString(query)}&page={page}&page_size={pageSize}",
        cancellationToken);

    if (!searchResponse.IsSuccessStatusCode)
    {
        return Results.Problem("RAWG search request failed.", statusCode: StatusCodes.Status502BadGateway);
    }

    var rawgSearch = await searchResponse.Content.ReadFromJsonAsync<RawgGamesResponse>(cancellationToken);
    var hits = rawgSearch?.Results ?? [];
    var count = rawgSearch?.Count ?? 0;

    var detailTasks = hits.Select(game =>
        GetGameDetailAsync(rawgClient, steamClient, rawgApiKey, game.Id.ToString(), countryCode, cancellationToken));
    var details = await Task.WhenAll(detailTasks);
    var results = details.Where(detail => detail is not null).Cast<GameDetailDto>().ToArray();

    return Results.Ok(new GameDetailSearchResponse
    {
        Count = count,
        Page = page,
        PageSize = pageSize,
        TotalPages = GetTotalPages(count, pageSize),
        HasNext = HasNextPage(count, page, pageSize),
        HasPrevious = page > 1,
        Next = HasNextPage(count, page, pageSize)
            ? BuildSearchPageLink(query, page + 1, pageSize, countryCode)
            : null,
        Previous = page > 1
            ? BuildSearchPageLink(query, page - 1, pageSize, countryCode)
            : null,
        Results = results
    });
})
.WithName("SearchGameDetails")
.RequireRateLimiting("GameApi");

app.MapGet("/games/{id}", async (
    string id,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken,
    string countryCode = "US") =>
{
    if (string.IsNullOrWhiteSpace(id))
    {
        return Results.BadRequest(new { message = "Game id or slug is required." });
    }

    var rawgApiKey = ApiKeys.Get(configuration, "Rawg");
    if (string.IsNullOrWhiteSpace(rawgApiKey))
    {
        return Results.Problem("RAWG API key is not configured.", statusCode: StatusCodes.Status500InternalServerError);
    }

    var gameDetail = await GetGameDetailAsync(
        httpClientFactory.CreateClient("Rawg"),
        httpClientFactory.CreateClient("Steam"),
        rawgApiKey,
        id,
        countryCode,
        cancellationToken);

    if (gameDetail is null)
    {
        return Results.NotFound(new { message = "Game was not found." });
    }

    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetService<GameRuneDbContext>();
        if (db is not null)
        {
            await DbEndpoints.UpsertGameDetailAsync(gameDetail, id, db);
            await DbEndpoints.RecordPriceAsync(gameDetail, countryCode, db);
        }
    }
    catch
    {
    }

    return Results.Ok(gameDetail);
})
.WithName("GetGameDetail")
.RequireRateLimiting("GameApi");

app.Run();

static async Task<GameDetailDto?> GetGameDetailAsync(
    HttpClient rawgClient,
    HttpClient steamClient,
    string rawgApiKey,
    string gameId,
    string countryCode,
    CancellationToken cancellationToken)
{
    var gameResponse = await rawgClient.GetAsync($"games/{Uri.EscapeDataString(gameId)}?key={Uri.EscapeDataString(rawgApiKey)}", cancellationToken);
    if (!gameResponse.IsSuccessStatusCode)
    {
        return null;
    }

    var rawgGame = await gameResponse.Content.ReadFromJsonAsync<RawgGameDetail>(cancellationToken);
    if (rawgGame is null)
    {
        return null;
    }

    var steamAppId = await GetSteamAppIdAsync(rawgClient, rawgApiKey, gameId, cancellationToken);
    var steamPrice = steamAppId is null
        ? null
        : await GetSteamPriceAsync(steamClient, steamAppId.Value, countryCode, cancellationToken);
    var imageUrls = await GetGameImageUrlsAsync(rawgClient, rawgApiKey, gameId, rawgGame.BackgroundImage, cancellationToken);

    return new GameDetailDto
    {
        Id = rawgGame.Id,
        Name = rawgGame.Name,
        ImageUrl = rawgGame.BackgroundImage,
        ImageUrls = imageUrls,
        Description = rawgGame.DescriptionRaw ?? rawgGame.Description,
        Released = rawgGame.Released,
        Rating = rawgGame.Rating,
        SteamAppId = steamAppId,
        SteamPrice = steamPrice
    };
}

static async Task<string[]> GetGameImageUrlsAsync(
    HttpClient rawgClient,
    string rawgApiKey,
    string gameId,
    string? backgroundImage,
    CancellationToken cancellationToken)
{
    var imageUrls = new List<string>();
    if (!string.IsNullOrWhiteSpace(backgroundImage))
    {
        imageUrls.Add(backgroundImage);
    }

    var screenshotsResponse = await rawgClient.GetAsync(
        $"games/{Uri.EscapeDataString(gameId)}/screenshots?key={Uri.EscapeDataString(rawgApiKey)}",
        cancellationToken);

    if (!screenshotsResponse.IsSuccessStatusCode)
    {
        return [.. imageUrls.Distinct().Take(3)];
    }

    var screenshots = await screenshotsResponse.Content.ReadFromJsonAsync<RawgScreenshotsResponse>(cancellationToken);
    imageUrls.AddRange(
        screenshots?.Results
            .Select(screenshot => screenshot.Image)
            .Where(image => !string.IsNullOrWhiteSpace(image)) ?? []);

    return [.. imageUrls.Distinct().Take(3)];
}

static async Task<int?> GetSteamAppIdAsync(HttpClient rawgClient, string rawgApiKey, string gameId, CancellationToken cancellationToken)
{
    var storesResponse = await rawgClient.GetAsync($"games/{Uri.EscapeDataString(gameId)}/stores?key={Uri.EscapeDataString(rawgApiKey)}", cancellationToken);
    if (!storesResponse.IsSuccessStatusCode)
    {
        return null;
    }

    var stores = await storesResponse.Content.ReadFromJsonAsync<RawgGameStoresResponse>(cancellationToken);
    var steamStore = stores?.Results.FirstOrDefault(store =>
        store.StoreId == 1 ||
        string.Equals(store.Store?.Slug, "steam", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(store.Store?.Name, "Steam", StringComparison.OrdinalIgnoreCase));

    return steamStore?.Url is null ? null : ExtractSteamAppId(steamStore.Url);
}

static async Task<SteamPriceDto?> GetSteamPriceAsync(HttpClient steamClient, int steamAppId, string countryCode, CancellationToken cancellationToken)
{
    var safeCountryCode = string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode.Trim().ToUpperInvariant();
    var steamResponse = await steamClient.GetAsync(
        $"appdetails?appids={steamAppId}&cc={Uri.EscapeDataString(safeCountryCode)}&filters=basic,price_overview",
        cancellationToken);

    if (!steamResponse.IsSuccessStatusCode)
    {
        return null;
    }

    var steamDetails = await steamResponse.Content.ReadFromJsonAsync<Dictionary<string, SteamAppDetailsResponse>>(cancellationToken);
    if (steamDetails is null ||
        !steamDetails.TryGetValue(steamAppId.ToString(), out var appDetails) ||
        !appDetails.Success ||
        appDetails.Data is null)
    {
        return null;
    }

    return appDetails.Data.PriceOverview ?? (appDetails.Data.IsFree
        ? new SteamPriceDto
        {
            Currency = safeCountryCode,
            Initial = 0,
            Final = 0,
            DiscountPercent = 0,
            FinalFormatted = "Free"
        }
        : null);
}

static int GetTotalPages(int count, int pageSize)
{
    if (pageSize <= 0 || count <= 0)
    {
        return 0;
    }

    return (int)Math.Ceiling(count / (double)pageSize);
}

static bool HasNextPage(int count, int page, int pageSize)
{
    if (pageSize <= 0 || count <= 0)
    {
        return false;
    }

    return page < GetTotalPages(count, pageSize);
}

static string BuildGamesPageLink(int page, int pageSize, string? search)
{
    var link = $"/games?page={page}&pageSize={pageSize}";
    if (!string.IsNullOrWhiteSpace(search))
    {
        link += $"&search={Uri.EscapeDataString(search)}";
    }

    return link;
}

static string BuildSearchPageLink(string query, int page, int pageSize, string countryCode)
{
    return $"/games/search/details?query={Uri.EscapeDataString(query)}&page={page}&pageSize={pageSize}&countryCode={Uri.EscapeDataString(countryCode ?? "US")}";
}

static int? ExtractSteamAppId(string storeUrl)
{
    if (!Uri.TryCreate(storeUrl, UriKind.Absolute, out var uri))
    {
        return null;
    }

    var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    var appSegmentIndex = Array.FindIndex(segments, segment => string.Equals(segment, "app", StringComparison.OrdinalIgnoreCase));
    if (appSegmentIndex < 0 || appSegmentIndex + 1 >= segments.Length)
    {
        return null;
    }

    return int.TryParse(segments[appSegmentIndex + 1], out var appId) ? appId : null;
}
