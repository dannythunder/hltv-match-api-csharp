namespace HLTVService.Configuration;

public sealed class HltvOptions
{
    public BrowserlessOptions Browserless { get; set; } = new();
    public ScraperOptions Scraper { get; set; } = new();
    public CaptchaOptions Captcha { get; set; } = new();
}

public sealed class BrowserlessOptions
{
    public string Url { get; set; } = "ws://localhost:3000";
    public bool Enabled { get; set; } = true;
}

public sealed class ScraperOptions
{
    public bool Enabled { get; set; } = true;
    public int PageTimeout { get; set; } = 1_800_000;
    public int RandomVariance { get; set; } = 300_000;
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
}

public sealed class CaptchaOptions
{
    public string? TwoCaptchaApiKey { get; set; }
    public bool Enabled { get; set; } = true;
}
