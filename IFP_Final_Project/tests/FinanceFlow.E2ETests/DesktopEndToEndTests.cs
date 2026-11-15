using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Data.Sqlite;
using Microsoft.Playwright;

namespace FinanceFlow.E2ETests;

/// <summary>
/// The regression test of the published Windows application of BarsiG.
///
/// The test starts the real <c>BarsiG.exe</c>, opens the address of its server in a real
/// browser and repeats the two operations that failed before: the sign in and the
/// registration. It proves that the files of the framework of Blazor are delivered, that
/// the interface is interactive, that a new customer is created exactly once and that the
/// sign in still works after a restart of the program.
///
/// The database of the application is never deleted, the test only reads it.
/// </summary>
[Collection(FinanceFlowCollection.Name)]
public sealed class DesktopEndToEndTests
{
    private const string AddressFileName = "barsig-server.url";
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private const int SwMaximize = 3;
    private static readonly Dictionary<string, Process> RunningApplications = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string TestPublishDirectoryName = $"desktop-e2e-{Guid.NewGuid():N}";

    /// <summary>Creates the tests.</summary>
    public DesktopEndToEndTests()
    {
    }

    [Fact]
    public async Task ThePublishedApplication_SignsInAndRegistersWithoutAnHttpError()
    {
        string publishDirectory = EnsurePublished();

        string? url = await RunApplicationAsync(publishDirectory);

        Assert.NotNull(url);

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });

        await using IBrowserContext context = await browser.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = url, ViewportSize = new ViewportSize { Width = 1440, Height = 900 } });

        IPage page = await context.NewPageAsync();

        // 1. The interface of the published application is interactive.
        await page.GotoAsync("/login");

        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        string content = await page.ContentAsync();

        Assert.Contains("BarsiG", content);
        Assert.DoesNotContain("HTTP ERROR 400", content, StringComparison.OrdinalIgnoreCase);

        // 1a. The protection against the cross site request forgery is still active:
        // a request without the antiforgery token is rejected.
        using (HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) })
        {
            HttpResponseMessage rejected = await client.PostAsync(
                $"{url}/login",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["email"] = FinanceFlowFixture.DemoEmail,
                    ["password"] = FinanceFlowFixture.DemoPassword
                }));

            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        // The isolated Desktop build has no seeded demonstration customer.
        // Register a user, verify explicit sign-in, then test persistence after restart.
        string email = $"desktop-{Guid.NewGuid():N}@barsig.test";

        await RegisterAndSignInAsync(page, "Desktop Customer", email);

        string afterRegistration = await page.ContentAsync();

        Assert.DoesNotContain("HTTP ERROR 400", afterRegistration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dashboard", afterRegistration);

        Assert.Equal(1, CountCustomers(publishDirectory, email));

        await page.ClickAsync("button:has-text('Sign out')");
        await page.WaitForURLAsync(url => url!.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        await page.FillAsync("#login-identifier", email);
        await page.FillAsync("#login-password", "Secret123");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => !url!.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });

        // The customer can sign in again after a restart of the program.
        await StopApplicationAsync(publishDirectory);

        string? urlAfterRestart = await RunApplicationAsync(publishDirectory);

        Assert.NotNull(urlAfterRestart);

        await using IBrowserContext second = await browser.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = urlAfterRestart, ViewportSize = new ViewportSize { Width = 1440, Height = 900 } });

        IPage secondPage = await second.NewPageAsync();

        await secondPage.GotoAsync("/login");
        await FinanceFlowFixture.WaitForInteractiveAsync(secondPage);
        await secondPage.WaitForSelectorAsync("#login-identifier", new PageWaitForSelectorOptions { Timeout = 30_000 });

        await secondPage.FillAsync("#login-identifier", email);
        await secondPage.FillAsync("#login-password", "Secret123");
        await secondPage.ClickAsync("button[type=submit]");

        await secondPage.WaitForURLAsync(url => !url!.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });

        string afterRestart = await secondPage.ContentAsync();

        Assert.DoesNotContain("HTTP ERROR 400", afterRestart, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dashboard", afterRestart);

        // The database of the application keeps its content, nothing was deleted.
        Assert.Equal(1, CountCustomers(publishDirectory, email));

        await StopApplicationAsync(publishDirectory);
    }

    [Fact]
    public async Task ThePublishedApplication_SecondLaunchActivatesOneExistingWindow()
    {
        string publishDirectory = EnsurePublished();
        string? url = await RunApplicationAsync(publishDirectory);

        Assert.NotNull(url);
        Process primary = RunningApplications[publishDirectory];

        try
        {
            IntPtr[] initialWindows = await WaitForVisibleWindowsAsync(primary.Id, expectedCount: 1);
            IntPtr window = Assert.Single(initialWindows);

            await LaunchSecondInstanceAsync(publishDirectory);

            Assert.False(primary.HasExited);
            Assert.Equal(new Uri(url).GetLeftPart(UriPartial.Authority), (await File.ReadAllTextAsync(
                Path.Combine(publishDirectory, AddressFileName))).Trim());
            Assert.Single(await WaitForVisibleWindowsAsync(primary.Id, expectedCount: 1));

            _ = ShowWindow(window, SwMinimize);
            await WaitUntilAsync(() => IsIconic(window));

            await LaunchSecondInstanceAsync(publishDirectory);

            Assert.False(primary.HasExited);
            await WaitUntilAsync(() => !IsIconic(window));
            Assert.Single(await WaitForVisibleWindowsAsync(primary.Id, expectedCount: 1));

            _ = ShowWindow(window, SwMaximize);
            await WaitUntilAsync(() => IsZoomed(window));
            _ = ShowWindow(window, SwRestore);
            await WaitUntilAsync(() => !IsZoomed(window));
        }
        finally
        {
            await StopApplicationAsync(publishDirectory);
        }
    }

    [Fact]
    public async Task ThePublishedApplication_ShowsEveryPageOfTheBankAndSignsOut()
    {
        string publishDirectory = EnsurePublished();

        string? url = await RunApplicationAsync(publishDirectory);

        Assert.NotNull(url);

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });

        await using IBrowserContext context = await browser.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = url, ViewportSize = new ViewportSize { Width = 1440, Height = 900 } });

        IPage page = await context.NewPageAsync();

        string email = $"desktop-pages-{Guid.NewGuid():N}@barsig.test";
        await RegisterAndSignInAsync(page, "Desktop Page Customer", email);

        // Every page of the bank opens without an error and the session stays alive.
        (string Path, string Marker)[] pages =
        [
            ("/", "Dashboard"),
            ("/accounts", "My accounts"),
            ("/transfers", "Transfers"),
            ("/deposits", "Savings deposits"),
            ("/cards", "Virtual cards"),
            ("/security", "Security"),
            ("/transactions", "Transactions"),
            ("/budgets", "Budgets"),
            ("/analytics", "Analytics"),
            ("/forecast", "Forecast"),
            ("/insights", "Smart insights"),
            ("/goals", "Savings goals")
        ];

        foreach ((string path, string marker) in pages)
        {
            await page.GotoAsync(path);
            await FinanceFlowFixture.WaitForInteractiveAsync(page);
            await Task.Delay(600);

            string content = await page.ContentAsync();

            Assert.DoesNotContain("HTTP ERROR 400", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("HTTP ERROR 500", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(marker, content);
            Assert.DoesNotContain("/login", page.Url);
        }

        // The sign out returns to the page of the sign in.
        await page.GotoAsync("/");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        await page.ClickAsync("button:has-text('Sign out')");
        await page.WaitForURLAsync(url => url!.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });

        await StopApplicationAsync(publishDirectory);
    }

    /// <summary>Reads the number of the customers with an e-mail address from the database.</summary>
    private static int CountCustomers(string publishDirectory, string email)
    {
        string database = Path.Combine(publishDirectory, "financeflow.db");

        if (!File.Exists(database))
        {
            return 0;
        }

        using SqliteConnection connection = new($"Data Source={database};Mode=ReadOnly");

        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Users WHERE Email = $email";
        command.Parameters.AddWithValue("$email", email.ToLowerInvariant());

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static async Task RegisterAndSignInAsync(IPage page, string displayName, string email)
    {
        string phone = $"777 {Random.Shared.Next(100, 1000)} {Random.Shared.Next(10, 100)} {Random.Shared.Next(10, 100)}";

        await page.GotoAsync("/register");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.WaitForSelectorAsync("#register-name", new PageWaitForSelectorOptions { Timeout = 30_000 });
        await page.FillAsync("#register-name", displayName);
        await page.FillAsync("#register-email", email);
        await page.FillAsync("#register-phone", phone);
        await page.FillAsync("#register-password", "Secret123");
        await page.FillAsync("#register-confirm", "Secret123");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForSelectorAsync("#phone-demo-code", new PageWaitForSelectorOptions { Timeout = 30_000 });

        string code = (await page.Locator("[aria-label='Local demo verification code']").InnerTextAsync()).Trim();
        await page.FillAsync("#phone-demo-code", code);
        await page.ClickAsync("button:has-text('Verify and continue to sign in')");
        await page.WaitForURLAsync(url => url!.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        await page.FillAsync("#login-identifier", email);
        await page.FillAsync("#login-password", "Secret123");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => !url!.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
    }

    /// <summary>Starts the published application and waits until its server answers.</summary>
    private static async Task<string?> RunApplicationAsync(string publishDirectory)
    {
        string addressFile = Path.Combine(publishDirectory, AddressFileName);

        if (File.Exists(addressFile))
        {
            File.Delete(addressFile);
        }

        ProcessStartInfo startInfo = new(Path.Combine(publishDirectory, "BarsiG.exe"))
        {
            WorkingDirectory = publishDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The published application could not be started.");

        RunningApplications[publishDirectory] = process;

        try
        {
            for (int attempt = 0; attempt < 120; attempt++)
            {
                if (File.Exists(addressFile))
                {
                    string url = (await File.ReadAllTextAsync(addressFile)).Trim();

                    if (url.Length > 0 && await IsAliveAsync(url))
                    {
                        return url;
                    }
                }

                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"The published application stopped with the code {process.ExitCode}.");
                }

                await Task.Delay(500);
            }

            throw new TimeoutException("The published application did not start its server.");
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            RunningApplications.Remove(publishDirectory);
            process.Dispose();
            throw;
        }
    }

    /// <summary>Stops the published application.</summary>
    private static async Task StopApplicationAsync(string publishDirectory)
    {
        if (!RunningApplications.Remove(publishDirectory, out Process? process))
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();

                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                }
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>Checks that the server of the application answers.</summary>
    private static async Task<bool> IsAliveAsync(string url)
    {
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };

            HttpResponseMessage response = await client.GetAsync($"{url}/login");

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static async Task LaunchSecondInstanceAsync(string publishDirectory)
    {
        ProcessStartInfo startInfo = new(Path.Combine(publishDirectory, "BarsiG.exe"))
        {
            WorkingDirectory = publishDirectory,
            UseShellExecute = false
        };

        using Process second = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The second BarsiG process could not be started.");

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        try
        {
            await second.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!second.HasExited)
            {
                second.Kill(entireProcessTree: true);
                await second.WaitForExitAsync();
            }

            throw;
        }

        Assert.Equal(0, second.ExitCode);
    }

    private static async Task<IntPtr[]> WaitForVisibleWindowsAsync(int processId, int expectedCount)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            IntPtr[] windows = GetVisibleWindows(processId);

            if (windows.Length == expectedCount)
            {
                return windows;
            }

            await Task.Delay(100);
        }

        return GetVisibleWindows(processId);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.True(condition(), "The desktop window did not reach the expected state.");
    }

    private static IntPtr[] GetVisibleWindows(int processId)
    {
        List<IntPtr> windows = [];

        EnumWindowsCallback callback = (window, state) =>
        {
            _ = state;
            GetWindowThreadProcessId(window, out uint ownerProcessId);

            if (ownerProcessId == processId && IsWindowVisible(window))
            {
                windows.Add(window);
            }

            return true;
        };

        _ = EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        return [.. windows];
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    /// <summary>
    /// Makes sure that the published application exists. When it is missing, it is published,
    /// so that the test always measures the real artifact.
    /// </summary>
    private static string EnsurePublished()
    {
        string solution = FindSolutionDirectory();
        // Desktop end-to-end tests must never use the user's regular publish directory.
        // The app creates a fresh local database in this test-only folder.
        string publishDirectory = Path.Combine(solution, "publish", TestPublishDirectoryName);
        string executable = Path.Combine(publishDirectory, "BarsiG.exe");

        if (File.Exists(executable))
        {
            return publishDirectory;
        }

        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = solution,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(Path.Combine(solution, "src", "FinanceFlow.Desktop"));
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(publishDirectory);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The publish of the desktop application could not be started.");

        StringBuilder output = new();

        process.StandardOutput.ReadToEndAsync().ContinueWith(task => output.AppendLine(task.Result));
        process.StandardError.ReadToEndAsync().ContinueWith(task => output.AppendLine(task.Result));

        process.WaitForExit(600_000);

        if (!File.Exists(executable))
        {
            throw new InvalidOperationException(
                $"The published application was not created. The output of the publish:{Environment.NewLine}{output}");
        }

        return publishDirectory;
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
