using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using Microsoft.Playwright;

namespace FinanceFlow.E2ETests;

/// <summary>
/// Starts the real web application and a real browser for the end to end tests.
/// The application listens on a free port and uses its own database file,
/// therefore the tests never touch the database of a working installation.
/// </summary>
public sealed class FinanceFlowFixture : IAsyncLifetime
{
    private Process? _webProcess;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private StreamWriter? _logWriter;

    /// <summary>The address of the running application.</summary>
    public string BaseUrl { get; private set; } = string.Empty;
    public string DatabasePath { get; private set; } = string.Empty;

    /// <summary>The demonstration account of the seeded data.</summary>
    public const string DemoEmail = "demo@financeflow.app";

    /// <summary>The password of the demonstration account.</summary>
    public const string DemoPassword = "Barsig-E2E-Demo-2026!";

    /// <summary>The e-mail address of the administrator of the Demo Bank during the tests.</summary>
    public const string AdminEmail = "admin@barsig.test";

    /// <summary>The password of the administrator of the Demo Bank during the tests.</summary>
    public const string AdminPassword = "BarsiG_E2E_Test_2026!";

    /// <summary>
    /// The configuration of the build that is running the tests. The application has to be
    /// started with the same configuration, otherwise the tests would measure an old build.
    /// </summary>
    public static string Configuration =>
        AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        int port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}";

        string solutionDirectory = FindSolutionDirectory();
        string webProject = Path.Combine(solutionDirectory, "src", "FinanceFlow.Web");
        string databasePath = Path.Combine(Path.GetTempPath(), $"financeflow-e2e-{Guid.NewGuid():N}.db");
        DatabasePath = databasePath;

        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = webProject,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(webProject);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(Configuration);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(BaseUrl);

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["DemoData__EnableSeedAccount"] = "true";
        startInfo.Environment["BARSIG_DEMO_PASSWORD"] = DemoPassword;
        // Keep real throttling logic enabled while avoiding minute-long browser tests.
        // The application clamps this setting to 1–60 seconds and ignores it outside Development.
        startInfo.Environment["PhoneVerification__ResendCooldownSeconds"] = "2";
        startInfo.Environment["ConnectionStrings__FinanceFlow"] = $"Data Source={databasePath}";
        startInfo.Environment["BARSIG_ADMIN_EMAIL"] = AdminEmail;
        startInfo.Environment["BARSIG_ADMIN_PASSWORD"] = AdminPassword;

        // The whole test suite signs in many times from the same address, therefore the
        // limiter of the server is opened for the tests. The protection against a brute
        // force stays active: the throttle of the account is not changed.
        startInfo.Environment["Banking__RateLimit__SignInPerMinute"] = "1000";
        startInfo.Environment["Banking__RateLimit__MoneyPerMinute"] = "1000";

        _webProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The web application could not be started.");

        // The output of the application has to be read continuously. Otherwise the pipe
        // fills up, the server blocks and every test would time out.
        string logPath = Path.Combine(Path.GetTempPath(), "financeflow-e2e-server.log");

        _logWriter = new StreamWriter(logPath, append: true) { AutoFlush = true };
        _ = Task.Run(async () => await CopyAsync(_webProcess.StandardOutput, _logWriter));
        _ = Task.Run(async () => await CopyAsync(_webProcess.StandardError, _logWriter));

        await WaitForServerAsync();

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <summary>Creates a new browser context with a fresh cookie container.</summary>
    public async Task<IBrowserContext> CreateContextAsync()
    {
        if (_browser is null)
        {
            throw new InvalidOperationException("The browser was not started.");
        }

        return await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
    }

    /// <summary>
    /// Waits until the page is fully interactive. Blazor replaces the parts of the page
    /// that were rendered on the server, and the replacement is finished when the
    /// markers of the server render are gone.
    /// </summary>
    public static async Task WaitForInteractiveAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => window.__financeFlowReady === true",
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    /// <summary>Signs a new browser context in with the demonstration account.</summary>
    public async Task SignInAsync(IBrowserContext context)
    {
        IPage page = await context.NewPageAsync();

        try
        {
            await page.GotoAsync("/login");
            await WaitForInteractiveAsync(page);
            await page.WaitForSelectorAsync("#login-identifier", new PageWaitForSelectorOptions { Timeout = 15_000 });
            await page.FillAsync("#login-identifier", DemoEmail);
            await page.FillAsync("#login-password", DemoPassword);
            await page.ClickAsync("button[type=submit]");

            await page.WaitForURLAsync(url => !url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        }
        catch (Exception exception)
        {
            string content = await page.ContentAsync();

            await File.WriteAllTextAsync(
                Path.Combine(Path.GetTempPath(), "financeflow-e2e-failure.html"),
                content);

            Console.WriteLine($"SIGN IN FAILED AT {page.Url}");
            Console.WriteLine($"CONTENT LENGTH: {content.Length}");

            throw new InvalidOperationException($"The sign in failed: {exception.Message}", exception);
        }

        await page.CloseAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;

        if (_webProcess is not null)
        {
            try
            {
                if (!_webProcess.HasExited)
                {
                    _webProcess.Kill(entireProcessTree: true);
                }

                await _webProcess.WaitForExitAsync();
            }
            catch (InvalidOperationException)
            {
                // The process was already gone.
            }

            _webProcess.Dispose();
            _webProcess = null;
        }

        _logWriter?.Dispose();
        _logWriter = null;
    }

    private static async Task CopyAsync(StreamReader reader, StreamWriter writer)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            await writer.WriteLineAsync(line);
        }
    }

    private async Task WaitForServerAsync()
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };

        for (int attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync($"{BaseUrl}/login");

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The server is not ready yet.
            }
            catch (TaskCanceledException)
            {
                // The server is not ready yet.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"The web application did not start on {BaseUrl}.");
    }

    private static int GetFreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    private static string FindSolutionDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.GetFiles("*.sln").Length > 0 || directory.GetFiles("*.slnx").Length > 0)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The solution file could not be found.");
    }
}

/// <summary>Groups the tests that share one running application.</summary>
[CollectionDefinition(Name)]
public sealed class FinanceFlowCollection : ICollectionFixture<FinanceFlowFixture>
{
    /// <summary>The name of the collection.</summary>
    public const string Name = "FinanceFlow end to end tests";
}
