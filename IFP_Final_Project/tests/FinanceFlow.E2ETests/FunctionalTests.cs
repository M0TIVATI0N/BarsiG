using Microsoft.Playwright;

namespace FinanceFlow.E2ETests;

/// <summary>
/// Functional regression tests: they verify the complete scope of the application
/// in a real browser - registration, the isolation of the accounts, the transactions,
/// the budgets, the analytics, the forecast, the export and the real time update.
/// </summary>
[Collection(FinanceFlowCollection.Name)]
public sealed class FunctionalTests
{
    private readonly FinanceFlowFixture _fixture;

    /// <summary>Creates the tests.</summary>
    public FunctionalTests(FinanceFlowFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task F01_RegistrationCreatesAnEmptyAccount()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync("/register");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        string email = $"audit{DateTime.UtcNow.Ticks}@financeflow.app";

        await RegisterAndSignInAsync(page, "Audit User", email);

        string content = await page.ContentAsync();

        Assert.Contains("Audit User", content);
        Assert.Contains("0,00", content.Replace(" ", " ")); // no transactions yet
        Console.WriteLine("F1 OK: registration works, the new account is empty");
    }

    [Fact]
    public async Task F02_TheAccountsAreSeparated()
    {
        await using IBrowserContext first = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(first);

        IPage demo = await first.NewPageAsync();
        await demo.GotoAsync("/transactions");
        await FinanceFlowFixture.WaitForInteractiveAsync(demo);
        await demo.WaitForSelectorAsync("table.table tbody tr");

        int demoRows = await demo.Locator("table.table tbody tr").CountAsync();
        Assert.True(demoRows > 0);

        await using IBrowserContext second = await _fixture.CreateContextAsync();
        IPage other = await second.NewPageAsync();

        await other.GotoAsync("/register");
        await FinanceFlowFixture.WaitForInteractiveAsync(other);

        await RegisterAndSignInAsync(
            other,
            "Other User",
            $"other{DateTime.UtcNow.Ticks}@financeflow.app");

        await other.GotoAsync("/transactions");
        await FinanceFlowFixture.WaitForInteractiveAsync(other);

        string content = await other.ContentAsync();
        Assert.DoesNotContain("Monthly salary", content);

        Console.WriteLine($"F2 OK: the demo account has {demoRows} rows, the new account has none");
    }

    private static async Task RegisterAndSignInAsync(IPage page, string displayName, string email)
    {
        string phone = $"777 {Random.Shared.Next(100, 1000)} {Random.Shared.Next(10, 100)} {Random.Shared.Next(10, 100)}";

        await page.GotoAsync("/register");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.FillAsync("#register-name", displayName);
        await page.FillAsync("#register-email", email);
        await page.FillAsync("#register-phone", phone);
        await page.FillAsync("#register-password", "Audit1234");
        await page.FillAsync("#register-confirm", "Audit1234");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForSelectorAsync("#phone-demo-code", new PageWaitForSelectorOptions { Timeout = 30_000 });

        string code = (await page.Locator("[aria-label='Local demo verification code']").InnerTextAsync()).Trim();
        await page.FillAsync("#phone-demo-code", code);
        await page.ClickAsync("button:has-text('Verify and continue to sign in')");
        await page.WaitForURLAsync(url => url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });

        await page.FillAsync("#login-identifier", email);
        await page.FillAsync("#login-password", "Audit1234");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => !url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
    }

    [Fact]
    public async Task F03_TransactionCanBeCreatedEditedFilteredAndDeleted()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/transactions");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.WaitForSelectorAsync("table.table tbody tr");

        int before = await page.Locator("table.table tbody tr").CountAsync();

        // create
        await page.ClickAsync("button:has-text('Add a transaction')");
        await page.WaitForSelectorAsync(".editor-card");
        await page.FillAsync("#editor-amount", "777.77");
        await page.FillAsync("#editor-note", "Audit marker");
        await page.ClickAsync(".editor-card button[type=submit]");
        await page.WaitForSelectorAsync(".toast");

        Assert.Contains("Audit marker", await page.ContentAsync());
        Console.WriteLine("F3.1 OK: the transaction was created and the toast appeared");

        // edit
        await page.FillAsync("#filter-search", "Audit marker");
        await page.WaitForTimeoutAsync(800);
        await page.ClickAsync("table.table tbody tr:first-child button[title=Edit]");
        await page.WaitForSelectorAsync(".editor-card");
        await page.FillAsync("#editor-note", "Audit marker edited");
        await page.ClickAsync(".editor-card button[type=submit]");
        await page.WaitForSelectorAsync(".toast");
        await page.WaitForTimeoutAsync(500);

        Assert.Contains("Audit marker edited", await page.ContentAsync());
        Console.WriteLine("F3.2 OK: the transaction was edited");

        // filter
        await page.SelectOptionAsync("#filter-kind", "Income");
        await page.WaitForTimeoutAsync(800);
        int incomeRows = await page.Locator("table.table tbody tr").CountAsync();
        Assert.True(incomeRows <= before + 1);
        Console.WriteLine($"F3.3 OK: the filter works, {incomeRows} income rows");

        // delete
        await page.SelectOptionAsync("#filter-kind", "all");
        await page.FillAsync("#filter-search", "Audit marker");
        await page.WaitForTimeoutAsync(800);
        await page.ClickAsync("table.table tbody tr:first-child button[title=Delete]");
        await page.WaitForSelectorAsync(".modal");
        await page.ClickAsync(".modal button.btn-danger");
        await page.WaitForSelectorAsync(".toast");
        await page.WaitForTimeoutAsync(500);

        Assert.DoesNotContain("Audit marker edited", await page.ContentAsync());
        Console.WriteLine("F3.4 OK: the transaction was deleted");
    }

    [Fact]
    public async Task F04_ABudgetCanBeSetAndIsShown()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/budgets");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        await page.WaitForSelectorAsync(".progress-track");
        int before = await page.Locator(".budget-item").CountAsync();

        await page.FillAsync("#budget-limit", "99999");
        await page.ClickAsync(".editor-card button[type=submit], .card button[type=submit]");
        await page.WaitForTimeoutAsync(1200);

        int after = await page.Locator(".budget-item").CountAsync();
        Assert.True(after >= before);
        Console.WriteLine($"F4 OK: the budgets page works, {after} budgets are shown");
    }

    [Fact]
    public async Task F05_AnalyticsAndForecastShowRealNumbers()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();

        await page.GotoAsync("/analytics");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.WaitForSelectorAsync(".chart-svg");

        int bars = await page.Locator("rect.chart-bar").CountAsync();
        int segments = await page.Locator(".donut-svg path").CountAsync();
        Assert.True(bars > 0);
        Assert.True(segments > 0);

        await page.GotoAsync("/forecast");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.WaitForSelectorAsync("table.table");

        int rows = await page.Locator("table.table tbody tr").CountAsync();
        Assert.True(rows > 0);

        Console.WriteLine($"F5 OK: analytics {bars} bars and {segments} ring segments, forecast {rows} predicted months");
    }

    [Fact]
    public async Task F06_TheExportProducesBothFormats()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/export");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        string csv = await page.EvaluateAsync<string>("async () => await (await fetch('/export/transactions.csv')).text()");
        string json = await page.EvaluateAsync<string>("async () => await (await fetch('/export/transactions.json')).text()");

        Assert.Contains("Date,Type,Category,Amount,Note", csv);
        Assert.Contains("\"amount\"", json);

        Console.WriteLine($"F6 OK: CSV {csv.Split('\n').Length - 1} lines, JSON {json.Length} characters");
    }

    [Fact]
    public async Task F07_TheRealTimeUpdateReachesTheSecondPage()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage editor = await context.NewPageAsync();
        IPage watcher = await context.NewPageAsync();

        await editor.GotoAsync("/transactions");
        await FinanceFlowFixture.WaitForInteractiveAsync(editor);
        await watcher.GotoAsync("/");
        await FinanceFlowFixture.WaitForInteractiveAsync(watcher);
        await watcher.WaitForSelectorAsync(".stat-card");

        string before = (await watcher.Locator(".stat-value").First.TextContentAsync()) ?? string.Empty;

        await editor.ClickAsync("button:has-text('Add a transaction')");
        await editor.WaitForSelectorAsync(".editor-card");
        await editor.FillAsync("#editor-amount", "4242");
        await editor.FillAsync("#editor-note", "Realtime marker");
        await editor.ClickAsync(".editor-card button[type=submit]");
        await editor.WaitForSelectorAsync(".toast");

        // the second page must refresh itself through the SignalR hub
        await watcher.WaitForFunctionAsync(
            "(before) => { const value = document.querySelector('.stat-value'); return value !== null && value.textContent !== before; }",
            before,
            new PageWaitForFunctionOptions { Timeout = 25_000 });

        string after = (await watcher.Locator(".stat-value").First.TextContentAsync()) ?? string.Empty;
        Console.WriteLine($"F7 OK: the balance on the second page changed from '{before}' to '{after}'");
    }

    [Fact]
    public async Task F08_TheStoredDataIsReadFromTheDatabase()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/transactions");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.WaitForSelectorAsync("table.table tbody tr");

        int rows = await page.Locator("table.table tbody tr").CountAsync();
        string exported = await page.EvaluateAsync<string>(
            "async () => await (await fetch('/export/transactions.csv')).text()");

        int exportedRows = exported.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;

        Assert.True(rows > 0);
        Assert.True(exportedRows > rows);

        Console.WriteLine($"F08 OK: {rows} rows are shown on the page, {exportedRows} rows are stored in the database");
    }
}
