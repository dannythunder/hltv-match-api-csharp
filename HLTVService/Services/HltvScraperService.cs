using System.Collections.Concurrent;
using System.Text.Json;
using HLTVService.Configuration;
using HLTVService.Models;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace HLTVService.Services;

public sealed class HltvScraperService : BackgroundService
{
    private readonly BrowserService _browserService;
    private readonly CaptchaSolverService _captchaSolver;
    private readonly ILogger<HltvScraperService> _logger;
    private readonly HltvOptions _options;
    private readonly ConcurrentDictionary<string, Match> _liveMatches = new();
    private readonly ConcurrentDictionary<string, Match> _upcomingMatches = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _missingMatchTracker = new();
    private readonly SemaphoreSlim _scrapeLock = new(1, 1);
    private IPage? _currentPage;
    private DateTimeOffset _lastPageRefresh = DateTimeOffset.MinValue;

    public HltvScraperService(
        BrowserService browserService,
        CaptchaSolverService captchaSolver,
        IOptions<HltvOptions> options,
        ILogger<HltvScraperService> logger)
    {
        _browserService = browserService;
        _captchaSolver = captchaSolver;
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyCollection<Match> GetLiveMatches()
    {
        return _liveMatches.Values.OrderBy(match => match.MatchTime).ThenBy(match => match.MatchId).ToArray();
    }

    public IReadOnlyCollection<Match> GetUpcomingMatches()
    {
        return _upcomingMatches.Values.OrderBy(match => match.MatchTime).ThenBy(match => match.MatchId).ToArray();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Scraper.Enabled)
        {
            _logger.LogInformation("HLTV scraper is disabled by configuration");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScrapeSessionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during HLTV scrape session");
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    private async Task ScrapeSessionAsync(CancellationToken stoppingToken)
    {
        if (!await _scrapeLock.WaitAsync(0, stoppingToken))
        {
            return;
        }

        try
        {
            _logger.LogInformation("Starting HLTV scraping session");
            _currentPage = await _browserService.CreateStealthPageAsync(stoppingToken);
            SetupWebSocketInterception(_currentPage);

            await NavigateWithRetryAsync("https://www.hltv.org/matches", stoppingToken);
            await HandleCaptchaAndWaitForLoadAsync(stoppingToken);
            await ParseMatchDataAsync();

            var variance = _options.Scraper.RandomVariance > 0
                ? Random.Shared.Next(-_options.Scraper.RandomVariance, _options.Scraper.RandomVariance)
                : 0;
            var duration = TimeSpan.FromMilliseconds(Math.Max(30_000, _options.Scraper.PageTimeout + variance));
            var stopAt = DateTimeOffset.Now.Add(duration);
            _lastPageRefresh = DateTimeOffset.Now;

            while (DateTimeOffset.Now < stopAt && !stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                if (_currentPage is null || _currentPage.IsClosed)
                {
                    break;
                }

                await ParseMatchDataAsync();
                var decision = ShouldRefreshPage();
                if (decision.ShouldRefresh)
                {
                    _logger.LogInformation("Refreshing HLTV page: {Reason}", decision.Reason);
                    await RefreshPageAsync(stoppingToken);
                    _lastPageRefresh = DateTimeOffset.Now;
                }
            }
        }
        finally
        {
            await CleanupPageAsync();
            _scrapeLock.Release();
        }
    }

    private async Task NavigateWithRetryAsync(string url, CancellationToken stoppingToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await Task.Delay(Random.Shared.Next(2_000, 5_000), stoppingToken);
                await _currentPage!.GotoAsync(url, new PageGotoOptions { Timeout = 60_000 });
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Navigation attempt {Attempt} failed", attempt);
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), stoppingToken);
            }
        }

        throw new InvalidOperationException($"Failed to navigate to {url}", lastException);
    }

    private async Task HandleCaptchaAndWaitForLoadAsync(CancellationToken stoppingToken)
    {
        if (_currentPage is null)
        {
            return;
        }

        if (await CheckForCaptchaAsync())
        {
            await HandleCaptchaAsync(stoppingToken);
        }

        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        await AcceptCookiesAsync();

        try
        {
            await _currentPage.WaitForSelectorAsync(".match-wrapper, .matches-list", new PageWaitForSelectorOptions { Timeout = 30_000 });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not find match sections on HLTV page");
        }

        try
        {
            await _currentPage.ClickAsync(".matches-sort-by-toggle-time", new PageClickOptions { Timeout = 5_000 });
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not sort matches by time");
        }

        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
    }

    private async Task AcceptCookiesAsync()
    {
        var selectors = new[]
        {
            "button:has-text('Allow all cookies')",
            "button:has-text('Accept')",
            ".acceptAll",
            "#onetrust-accept-btn-handler",
            "[id*='accept'][id*='btn']",
            "button[class*='accept']",
            "button[class*='cookie']"
        };

        foreach (var selector in selectors)
        {
            try
            {
                var button = _currentPage!.Locator(selector).First;
                await button.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 1_000 });
                await button.ClickAsync();
                return;
            }
            catch
            {
                // Try the next known consent selector.
            }
        }
    }

    private async Task<bool> CheckForCaptchaAsync()
    {
        return await _currentPage!.EvaluateAsync<bool>("""
            () => {
                if (document.title.includes('Just a moment') || document.title.includes('Attention Required')) return true;
                if (document.querySelector('#challenge-form') || document.querySelector('[name="cf-turnstile-response"]')) return true;
                const selectors = ['iframe[src*="captcha"]', 'iframe[src*="recaptcha"]', '.cf-challenge-running', '#challenge-form', '.g-recaptcha'];
                return selectors.some(selector => {
                    const el = document.querySelector(selector);
                    if (!el) return false;
                    const rect = el.getBoundingClientRect();
                    const style = window.getComputedStyle(el);
                    return rect.width > 0 && rect.height > 0 && style.display !== 'none' && style.visibility !== 'hidden' && style.opacity !== '0';
                });
            }
            """);
    }

    private async Task HandleCaptchaAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            var title = await _currentPage!.TitleAsync();
            if (!title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) &&
                !title.Contains("Attention Required", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (attempt % 3 == 0)
            {
                await SimulateHumanBehaviorAsync(stoppingToken);
            }
        }

        if (!await CheckForCaptchaAsync())
        {
            return;
        }

        var recaptchaKey = await _captchaSolver.DetectRecaptchaSiteKeyAsync(_currentPage!);
        if (!string.IsNullOrWhiteSpace(recaptchaKey) &&
            await _captchaSolver.SolveRecaptchaV2Async(_currentPage!, recaptchaKey, stoppingToken))
        {
            return;
        }

        var turnstileKey = await _captchaSolver.DetectTurnstileSiteKeyAsync(_currentPage!);
        if (!string.IsNullOrWhiteSpace(turnstileKey))
        {
            await _captchaSolver.SolveTurnstileAsync(_currentPage!, turnstileKey, stoppingToken);
        }
    }

    private async Task SimulateHumanBehaviorAsync(CancellationToken stoppingToken)
    {
        for (var i = 0; i < 5; i++)
        {
            await _currentPage!.Mouse.MoveAsync(Random.Shared.Next(100, 800), Random.Shared.Next(100, 600));
            await Task.Delay(Random.Shared.Next(100, 500), stoppingToken);
        }

        await _currentPage!.EvaluateAsync($"window.scrollBy(0, {Random.Shared.Next(100, 300)})");
    }

    private async Task ParseMatchDataAsync()
    {
        if (_currentPage is null || _currentPage.IsClosed)
        {
            return;
        }

        var liveMatches = await ParseMatchesAsync(true);
        foreach (var match in liveMatches)
        {
            _liveMatches[match.MatchId] = match;
            _upcomingMatches.TryRemove(match.MatchId, out _);
        }

        var upcomingMatches = await ParseMatchesAsync(false);
        foreach (var match in upcomingMatches)
        {
            _upcomingMatches[match.MatchId] = match;
        }

        _logger.LogInformation("Parsed {LiveCount} live and {UpcomingCount} upcoming matches", liveMatches.Count, upcomingMatches.Count);
    }

    private async Task<List<Match>> ParseMatchesAsync(bool isLive)
    {
        if (isLive)
        {
            try
            {
                await _currentPage!.WaitForSelectorAsync(".match-team-livescore .match-team-score", new PageWaitForSelectorOptions { Timeout = 5_000 });
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // There may simply be no live matches.
            }
        }

        var payload = await _currentPage!.EvaluateAsync<JsonElement>(MatchExtractionScript, isLive);
        var browserMatches = JsonSerializer.Deserialize<List<BrowserMatch>>(payload.GetRawText()) ?? [];
        var now = DateTimeOffset.Now;

        return browserMatches
            .Where(match => !string.IsNullOrWhiteSpace(match.MatchId) &&
                            !string.IsNullOrWhiteSpace(match.Team1Name) &&
                            !string.IsNullOrWhiteSpace(match.Team2Name))
            .Select(match => new Match
            {
                MatchId = match.MatchId!,
                Team1Name = match.Team1Name!,
                Team2Name = match.Team2Name!,
                Team1Logo = match.Team1Logo ?? "",
                Team2Logo = match.Team2Logo ?? "",
                Team1Score = match.Team1Score,
                Team2Score = match.Team2Score,
                Team1MapWins = match.Team1MapWins,
                Team2MapWins = match.Team2MapWins,
                Format = match.Format ?? "",
                Event = match.Event ?? "",
                MatchTime = match.MatchTimeUnix is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(match.MatchTimeUnix.Value).ToLocalTime(),
                MatchUrl = match.MatchUrl ?? "",
                IsLive = isLive,
                LastUpdated = now
            })
            .ToList();
    }

    private void SetupWebSocketInterception(IPage page)
    {
        page.WebSocket += (_, webSocket) =>
        {
            _logger.LogInformation("WebSocket connection detected: {Url}", webSocket.Url);
            webSocket.FrameReceived += (_, frame) => ParseWebSocketData(frame.Text);
        };
    }

    private void ParseWebSocketData(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            if (!root.TryGetProperty("matchId", out var matchIdElement) ||
                !root.TryGetProperty("score", out var scoreElement))
            {
                return;
            }

            var matchId = matchIdElement.GetString();
            if (string.IsNullOrWhiteSpace(matchId) || !_liveMatches.TryGetValue(matchId, out var match))
            {
                return;
            }

            if (scoreElement.TryGetProperty("team1", out var team1Score))
            {
                match.Team1Score = team1Score.GetInt32();
            }

            if (scoreElement.TryGetProperty("team2", out var team2Score))
            {
                match.Team2Score = team2Score.GetInt32();
            }

            match.LastUpdated = DateTimeOffset.Now;
        }
        catch (JsonException)
        {
            // Most Socket.IO frames are not direct score JSON payloads.
        }
    }

    private RefreshDecision ShouldRefreshPage()
    {
        var now = DateTimeOffset.Now;

        foreach (var match in _upcomingMatches.Values)
        {
            if (match.MatchTime is null)
            {
                continue;
            }

            var minutesUntilStart = (match.MatchTime.Value - now).TotalMinutes;
            if (minutesUntilStart is >= -1 and <= 2)
            {
                return new RefreshDecision(true, $"Match {match.MatchId} is starting soon");
            }

            if (minutesUntilStart >= -5 || _liveMatches.ContainsKey(match.MatchId))
            {
                _missingMatchTracker.TryRemove(match.MatchId, out _);
                continue;
            }

            var firstMissed = _missingMatchTracker.GetOrAdd(match.MatchId, now);
            var minutesMissing = (now - firstMissed).TotalMinutes;
            if (minutesMissing < 10 && (now - _lastPageRefresh).TotalMinutes >= 2)
            {
                return new RefreshDecision(true, $"Match {match.MatchId} should be live");
            }
        }

        if ((now - _lastPageRefresh).TotalMinutes >= 30)
        {
            return new RefreshDecision(true, "Regular 30-minute refresh");
        }

        return new RefreshDecision(false, null);
    }

    private async Task RefreshPageAsync(CancellationToken stoppingToken)
    {
        if (_currentPage is null || _currentPage.IsClosed)
        {
            return;
        }

        await NavigateWithRetryAsync("https://www.hltv.org/matches", stoppingToken);
        await HandleCaptchaAndWaitForLoadAsync(stoppingToken);
        await ParseMatchDataAsync();
    }

    private async Task CleanupPageAsync()
    {
        if (_currentPage is { IsClosed: false })
        {
            await _currentPage.CloseAsync();
        }

        _currentPage = null;
    }

    private sealed record RefreshDecision(bool ShouldRefresh, string? Reason);

    private const string MatchExtractionScript = """
        (isLive) => {
            const matches = [];
            const selector = isLive ? '.match-wrapper[live="true"]' : '.match-wrapper:not([live="true"])';
            const matchElements = document.querySelectorAll(selector);

            matchElements.forEach(matchEl => {
                try {
                    const matchLink = matchEl.querySelector('a[href*="/matches/"]');
                    const matchUrl = matchLink ? matchLink.href : '';
                    const matchId = matchEl.getAttribute('data-match-id') || matchUrl.split('/')[4] || '';

                    const teamEls = matchEl.querySelectorAll('.match-team');
                    const team1El = teamEls[0];
                    const team2El = teamEls[1];
                    const team1Name = team1El?.querySelector('.match-teamname')?.textContent?.trim() || '';
                    const team2Name = team2El?.querySelector('.match-teamname')?.textContent?.trim() || '';
                    const team1Logo = team1El?.querySelector('.match-team-logo')?.src || '';
                    const team2Logo = team2El?.querySelector('.match-team-logo')?.src || '';

                    let team1Score = null;
                    let team2Score = null;
                    let team1MapWins = null;
                    let team2MapWins = null;

                    if (isLive) {
                        const livescoreContainer = matchEl.querySelector('.match-team-livescore');
                        if (livescoreContainer) {
                            const team1MapWinEl = livescoreContainer.querySelector('[data-livescore-maps-won-for][data-livescore-team="' + matchEl.getAttribute('team1') + '"]');
                            const team2MapWinEl = livescoreContainer.querySelector('[data-livescore-maps-won-for][data-livescore-team="' + matchEl.getAttribute('team2') + '"]');
                            const scoreSpans = livescoreContainer.querySelectorAll('.current-map-score');

                            if (team1MapWinEl && /^\d+$/.test(team1MapWinEl.textContent?.trim() || '')) team1MapWins = parseInt(team1MapWinEl.textContent.trim());
                            if (team2MapWinEl && /^\d+$/.test(team2MapWinEl.textContent?.trim() || '')) team2MapWins = parseInt(team2MapWinEl.textContent.trim());
                            if (scoreSpans.length >= 2) {
                                const score1Text = scoreSpans[0]?.textContent?.trim() || '';
                                const score2Text = scoreSpans[1]?.textContent?.trim() || '';
                                if (/^\d+$/.test(score1Text)) team1Score = parseInt(score1Text);
                                if (/^\d+$/.test(score2Text)) team2Score = parseInt(score2Text);
                            }
                        }

                        if (team1Score === null || team2Score === null) {
                            const team1ScoreText = team1El?.querySelector('.match-team-score')?.textContent?.trim() || '';
                            const team2ScoreText = team2El?.querySelector('.match-team-score')?.textContent?.trim() || '';
                            if (/^\d+$/.test(team1ScoreText)) team1Score = parseInt(team1ScoreText);
                            if (/^\d+$/.test(team2ScoreText)) team2Score = parseInt(team2ScoreText);
                        }
                    }

                    const eventEl = matchEl.querySelector('.match-event');
                    const metaEl = matchEl.querySelector('.match-meta');
                    const timeEl = matchEl.querySelector('.match-time');
                    const matchTimeUnix = timeEl?.getAttribute('data-unix') ? parseInt(timeEl.getAttribute('data-unix')) : null;

                    if (matchId && team1Name && team2Name) {
                        matches.push({
                            matchId,
                            team1Name,
                            team2Name,
                            team1Logo,
                            team2Logo,
                            team1Score,
                            team2Score,
                            team1MapWins,
                            team2MapWins,
                            format: metaEl?.textContent?.trim() || '',
                            event: eventEl?.getAttribute('data-event-headline') || eventEl?.textContent?.trim() || '',
                            matchTimeUnix,
                            matchUrl,
                            isLive
                        });
                    }
                } catch (error) {
                    console.error('Error parsing match:', error);
                }
            });

            return matches;
        }
        """;
}
