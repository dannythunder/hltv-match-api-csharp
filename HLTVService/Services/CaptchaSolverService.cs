using System.Text.Json;
using HLTVService.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace HLTVService.Services;

public sealed class CaptchaSolverService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CaptchaSolverService> _logger;
    private readonly HltvOptions _options;

    public CaptchaSolverService(HttpClient httpClient, IOptions<HltvOptions> options, ILogger<CaptchaSolverService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<string?> DetectRecaptchaSiteKeyAsync(IPage page)
    {
        return await page.EvaluateAsync<string?>("""
            () => document.querySelector('.g-recaptcha')?.getAttribute('data-sitekey')
                || document.querySelector('[data-sitekey]')?.getAttribute('data-sitekey')
                || null
            """);
    }

    public async Task<string?> DetectTurnstileSiteKeyAsync(IPage page)
    {
        return await page.EvaluateAsync<string?>("""
            () => document.querySelector('.cf-turnstile')?.getAttribute('data-sitekey')
                || document.querySelector('[name="cf-turnstile-response"]')?.closest('[data-sitekey]')?.getAttribute('data-sitekey')
                || null
            """);
    }

    public async Task<bool> SolveRecaptchaV2Async(IPage page, string siteKey, CancellationToken cancellationToken)
    {
        return await SolveAsync(page, "userrecaptcha", siteKey, "googlekey", InjectRecaptchaSolutionAsync, cancellationToken);
    }

    public async Task<bool> SolveTurnstileAsync(IPage page, string siteKey, CancellationToken cancellationToken)
    {
        return await SolveAsync(page, "turnstile", siteKey, "sitekey", InjectTurnstileSolutionAsync, cancellationToken);
    }

    private async Task<bool> SolveAsync(
        IPage page,
        string method,
        string siteKey,
        string siteKeyParameter,
        Func<IPage, string, Task> inject,
        CancellationToken cancellationToken)
    {
        if (!_options.Captcha.Enabled || string.IsNullOrWhiteSpace(_options.Captcha.TwoCaptchaApiKey))
        {
            _logger.LogInformation("Captcha solving is disabled or missing TwoCaptcha API key");
            return false;
        }

        var submitUri = BuildUri("https://2captcha.com/in.php", new Dictionary<string, string?>
        {
            ["key"] = _options.Captcha.TwoCaptchaApiKey,
            ["method"] = method,
            [siteKeyParameter] = siteKey,
            ["pageurl"] = page.Url,
            ["json"] = "1"
        });

        using var submitResponse = await _httpClient.GetAsync(submitUri, cancellationToken);
        var submitJson = await JsonDocument.ParseAsync(await submitResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!submitResponse.IsSuccessStatusCode || submitJson.RootElement.GetProperty("status").GetInt32() != 1)
        {
            _logger.LogWarning("Failed to submit captcha to 2captcha");
            return false;
        }

        var captchaId = submitJson.RootElement.GetProperty("request").GetString();
        if (string.IsNullOrWhiteSpace(captchaId))
        {
            return false;
        }

        var solution = await PollForSolutionAsync(captchaId, cancellationToken);
        if (solution is null)
        {
            return false;
        }

        await inject(page, solution);
        return true;
    }

    private async Task<string?> PollForSolutionAsync(string captchaId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            var uri = BuildUri("https://2captcha.com/res.php", new Dictionary<string, string?>
            {
                ["key"] = _options.Captcha.TwoCaptchaApiKey,
                ["action"] = "get",
                ["id"] = captchaId,
                ["json"] = "1"
            });

            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (json.RootElement.GetProperty("status").GetInt32() == 1)
            {
                return json.RootElement.GetProperty("request").GetString();
            }

            var status = json.RootElement.GetProperty("request").GetString();
            if (!string.Equals(status, "CAPCHA_NOT_READY", StringComparison.Ordinal))
            {
                _logger.LogWarning("2captcha returned {Status}", status);
                return null;
            }
        }

        return null;
    }

    private static async Task InjectRecaptchaSolutionAsync(IPage page, string token)
    {
        await page.EvaluateAsync("""
            (token) => {
                const textarea = document.querySelector('#g-recaptcha-response, [name="g-recaptcha-response"]');
                if (textarea) {
                    textarea.value = token;
                    textarea.innerHTML = token;
                    textarea.dispatchEvent(new Event('change', { bubbles: true }));
                }
            }
            """, token);
    }

    private static async Task InjectTurnstileSolutionAsync(IPage page, string token)
    {
        await page.EvaluateAsync("""
            (token) => {
                const input = document.querySelector('[name="cf-turnstile-response"]');
                if (input) {
                    input.value = token;
                    input.dispatchEvent(new Event('change', { bubbles: true }));
                }
            }
            """, token);
    }

    private static Uri BuildUri(string baseUri, IReadOnlyDictionary<string, string?> query)
    {
        var builder = new UriBuilder(baseUri);
        builder.Query = string.Join("&", query
            .Where(pair => pair.Value is not null)
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
        return builder.Uri;
    }
}
