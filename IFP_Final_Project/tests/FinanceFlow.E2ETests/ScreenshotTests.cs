using Microsoft.Playwright;

namespace FinanceFlow.E2ETests;

/// <summary>
/// Takes the mockups of the pages for the presentation.
/// The test is not part of the daily test run, it is started on purpose:
/// <code>dotnet test tests/FinanceFlow.E2ETests --filter FullyQualifiedName~ScreenshotTests</code>
/// The pictures are written into the folder <c>docs/images</c>.
/// </summary>
[Collection(FinanceFlowCollection.Name)]
public sealed class ScreenshotTests
{
    private readonly FinanceFlowFixture _fixture;

    /// <summary>Creates the test.</summary>
    public ScreenshotTests(FinanceFlowFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>The folder of the pictures.</summary>
    private static string ImageDirectory
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("BARSIG_SCREENSHOT_DIR");
            string directory = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(FindSolutionDirectory(), "docs", "images")
                : Path.GetFullPath(configured);
            Directory.CreateDirectory(directory);

            return directory;
        }
    }

    [Theory]
    [InlineData("/login", "login")]
    [InlineData("/", "dashboard")]
    [InlineData("/transactions", "transactions")]
    [InlineData("/categories", "categories")]
    [InlineData("/budgets", "budgets")]
    [InlineData("/analytics", "analytics")]
    [InlineData("/forecast", "forecast")]
    [InlineData("/insights", "insights")]
    [InlineData("/goals", "goals")]
    [InlineData("/export", "export")]
    [InlineData("/accounts", "accounts")]
    [InlineData("/transfers", "transfers")]
    [InlineData("/deposits", "deposits")]
    [InlineData("/cards", "cards")]
    [InlineData("/security", "security")]
    public async Task TakeScreenshots(string path, string name)
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();

        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync(path);
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await Task.Delay(1200);

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ImageDirectory, $"{name}.png"),
            FullPage = true
        });
    }

    [Fact]
    public async Task TakeAMobileScreenshot()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();

        await page.SetViewportSizeAsync(430, 920);
        await page.GotoAsync("/");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await Task.Delay(1200);

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ImageDirectory, "dashboard-mobile.png"),
            FullPage = true
        });
    }

    [Fact]
    public async Task TakeAnAdminScreenshot()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();

        IPage page = await context.NewPageAsync();

        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync("/login");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.WaitForSelectorAsync("#login-identifier", new PageWaitForSelectorOptions { Timeout = 30_000 });

        await page.FillAsync("#login-identifier", FinanceFlowFixture.AdminEmail);
        await page.FillAsync("#login-password", FinanceFlowFixture.AdminPassword);
        await page.ClickAsync("button[type=submit]");

        await page.WaitForURLAsync(url => !url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });

        await page.GotoAsync("/admin");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await Task.Delay(1500);

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ImageDirectory, "admin.png"),
            FullPage = true
        });
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task TakeDashboardThemeScreenshot(string theme)
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1957);
        await page.GotoAsync("/");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        Assert.Equal(0, await page.Locator(".sidebar .brand img").CountAsync());
        Assert.Equal("BarsiG", (await page.Locator(".sidebar .brand-name").InnerTextAsync()).Trim());

        double[] before = await page.EvaluateAsync<double[]>(
            "() => [\".sidebar\", \".topbar\", \".page-header\", \".dashboard-banner\", \".stat-grid\", \".dashboard-chart-row\"]" +
            ".flatMap(selector => { const rect = document.querySelector(selector).getBoundingClientRect(); " +
            "return [rect.x, rect.y, rect.width, rect.height]; })");

        if (theme == "dark")
        {
            await page.Locator("button[title='Change the theme']").ClickAsync();
        }

        await page.WaitForFunctionAsync(
            "theme => document.documentElement.classList.contains('theme-dark') === (theme === 'dark') && " +
            "document.querySelector('.app-shell').classList.contains('theme-dark') === (theme === 'dark')",
            theme);

        double[] after = await page.EvaluateAsync<double[]>(
            "() => [\".sidebar\", \".topbar\", \".page-header\", \".dashboard-banner\", \".stat-grid\", \".dashboard-chart-row\"]" +
            ".flatMap(selector => { const rect = document.querySelector(selector).getBoundingClientRect(); " +
            "return [rect.x, rect.y, rect.width, rect.height]; })");
        Assert.Equal(before.Length, after.Length);
        for (int index = 0; index < before.Length; index++)
        {
            Assert.InRange(Math.Abs(before[index] - after[index]), 0, 0.5);
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ImageDirectory, $"dashboard-{theme}-reference-viewport.png"),
            FullPage = true
        });
    }

    [Theory]
    [InlineData(430, 900)]
    [InlineData(768, 900)]
    [InlineData(1280, 720)]
    [InlineData(1440, 900)]
    [InlineData(1920, 1080)]
    public async Task DashboardDoesNotOverflowCommonViewports(int width, int height)
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync("/");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        int horizontalOverflow = await page.EvaluateAsync<int>(
            "Math.max(document.documentElement.scrollWidth, document.body.scrollWidth) - window.innerWidth");

        Assert.True(
            horizontalOverflow <= 0,
            $"The dashboard overflows horizontally by {horizontalOverflow}px at {width}x{height}.");
    }

    private static string FindSolutionDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.GetFiles("*.sln").Length > 0)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The solution file could not be found.");
    }
}
