using GameListerBackend.Configuration;
using GameListerBackend.Models;

EnvFile.Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHttpClient("Rawg", client =>
{
    client.BaseAddress = new Uri("https://api.rawg.io/api/");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

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
            ImageUrl = game.BackgroundImage,
            Description = game.Description
        })
        .ToArray() ?? [];

    return Results.Ok(new GamesResponse
    {
        Count = rawgResponse?.Count ?? 0,
        Next = rawgResponse?.Next,
        Previous = rawgResponse?.Previous,
        Results = games
    });
})
.WithName("GetGames");

app.Run();
