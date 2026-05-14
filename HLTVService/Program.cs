using HLTVService.Configuration;
using HLTVService.Models;
using HLTVService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddOptions<HltvOptions>()
    .Bind(builder.Configuration.GetSection("Hltv"))
    .ValidateDataAnnotations();
builder.Services.AddHttpClient<CaptchaSolverService>();
builder.Services.AddSingleton<BrowserService>();
builder.Services.AddSingleton<HltvScraperService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<HltvScraperService>());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/api/matches", (HltvScraperService scraper) =>
    Results.Ok(new MatchResponse(scraper.GetLiveMatches(), scraper.GetUpcomingMatches())));

app.MapGet("/api/matches/live", (HltvScraperService scraper) =>
    Results.Ok(scraper.GetLiveMatches()));

app.MapGet("/api/matches/upcoming", (HltvScraperService scraper) =>
    Results.Ok(scraper.GetUpcomingMatches()));

app.MapGet("/api/mock/matches", () =>
    Results.Ok(new MatchResponse(MockMatches.Live(), MockMatches.Upcoming())));

app.MapGet("/api/mock/matches/live", () =>
    Results.Ok(MockMatches.Live()));

app.MapGet("/api/mock/matches/upcoming", () =>
    Results.Ok(MockMatches.Upcoming()));

app.Run();
