using HLTVService.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace HLTVService.Services;

public sealed class BrowserService : IAsyncDisposable
{
    private readonly ILogger<BrowserService> _logger;
    private readonly HltvOptions _options;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private bool _isShuttingDown;

    public BrowserService(IOptions<HltvOptions> options, ILogger<BrowserService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsBrowserAvailable => _browser is not null && !_isShuttingDown;
    public bool IsShuttingDown => _isShuttingDown;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (IsBrowserAvailable)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (IsBrowserAvailable)
            {
                return;
            }

            _logger.LogInformation("Initializing Playwright browser");
            _playwright = await Playwright.CreateAsync();

            if (_options.Browserless.Enabled)
            {
                var endpoint = _options.Browserless.Url;
                _logger.LogInformation("Connecting to Browserless at {Endpoint}", endpoint);
                _browser = await _playwright.Chromium.ConnectOverCDPAsync(endpoint);
            }
            else
            {
                _logger.LogInformation("Launching local Chromium browser");
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = false,
                    SlowMo = 50,
                    Timeout = 60_000
                });
            }
        }
        catch
        {
            await DisposeBrowserAsync();
            throw;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<IPage> CreateStealthPageAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        if (_browser is null)
        {
            throw new InvalidOperationException("Browser is not available.");
        }

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = _options.Scraper.UserAgent,
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            Locale = "en-US",
            TimezoneId = "America/New_York",
            Permissions = ["geolocation"],
            Geolocation = new Geolocation { Latitude = 40.7128f, Longitude = -74.0060f },
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "en-US,en;q=0.9",
                ["Accept-Encoding"] = "gzip, deflate, br",
                ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8"
            }
        });

        var page = await context.NewPageAsync();
        page.Console += (_, message) => _logger.LogDebug("Browser console [{Type}]: {Text}", message.Type, message.Text);
        page.PageError += (_, error) => _logger.LogWarning("Browser page error: {Error}", error);
        await page.AddInitScriptAsync(StealthScript);
        return page;
    }

    public async ValueTask DisposeAsync()
    {
        _isShuttingDown = true;
        await DisposeBrowserAsync();
        _initializationLock.Dispose();
    }

    private async Task DisposeBrowserAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
    }

    private const string StealthScript = """
        Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en'] });
        Object.defineProperty(navigator, 'webdriver', { get: () => false });
        if (!window.chrome) {
            window.chrome = { runtime: {} };
        }
        const originalQuery = window.navigator.permissions.query;
        window.navigator.permissions.query = (parameters) => (
            parameters.name === 'notifications'
                ? Promise.resolve({ state: Notification.permission })
                : originalQuery(parameters)
        );
        delete navigator.__proto__.webdriver;
        Object.defineProperty(navigator, 'plugins', {
            get: () => ({ 0: { description: "Portable Document Format", filename: "internal-pdf-viewer", name: "Chrome PDF Plugin" }, length: 1 })
        });
        """;
}
