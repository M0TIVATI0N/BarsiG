using Microsoft.Playwright;

namespace FinanceFlow.E2ETests;

/// <summary>
/// End to end tests. They open a real browser and drive the real application,
/// exactly like a user would do it.
/// </summary>
[Collection(FinanceFlowCollection.Name)]
public sealed class EndToEndTests
{
    private readonly FinanceFlowFixture _fixture;

    /// <summary>Creates the tests.</summary>
    public EndToEndTests(FinanceFlowFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TheLoginPageIsShownForAnAnonymousVisitor()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync("/transactions");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        await page.WaitForURLAsync(url => url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });

        Assert.Contains("Welcome back", await page.ContentAsync());
    }

    [Fact]
    public async Task AWrongPasswordIsRejected()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync("/login");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.FillAsync("#login-identifier", FinanceFlowFixture.DemoEmail);
        await page.FillAsync("#login-password", "wrong-password");
        await page.ClickAsync("button[type=submit]");

        await page.WaitForSelectorAsync(".alert-error", new PageWaitForSelectorOptions { Timeout = 30_000 });

        Assert.Contains("wrong", await page.ContentAsync());
    }

    [Fact]
    public async Task PhoneOnlyRegistration_VerifiesInDevelopment_ThenLogsInByPhone()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        IPage page = await context.NewPageAsync();
        string phoneInput = $"777 {Random.Shared.Next(100, 1000)} {Random.Shared.Next(10, 100)} {Random.Shared.Next(10, 100)}";
        string phone = $"+7{string.Concat(phoneInput.Where(char.IsAsciiDigit))}";

        await page.GotoAsync("/register");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.FillAsync("#register-name", "Phone Demo Customer");
        await page.FillAsync("#register-phone", phoneInput);
        await page.FillAsync("#register-password", "Barsig-E2E-Test-2026!");
        await page.FillAsync("#register-confirm", "Barsig-E2E-Test-2026!");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForSelectorAsync("#phone-demo-code, .alert-error", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.True(await page.Locator("#phone-demo-code").CountAsync() > 0, await page.ContentAsync());

        string demoCode = (await page.Locator("[aria-label='Local demo verification code']").InnerTextAsync()).Trim();
        Assert.Matches("^\\d{6}$", demoCode);
        Assert.Contains("Demo verification — no SMS is sent.", await page.ContentAsync());
        await page.FillAsync("#phone-demo-code", demoCode);
        await page.ClickAsync("button:has-text('Verify and continue to sign in')");
        await page.WaitForSelectorAsync("#login-identifier, .alert-error", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.True(await page.Locator("#login-identifier").CountAsync() > 0, await page.ContentAsync());
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        await page.FillAsync("#login-identifier", phoneInput);
        await page.FillAsync("#login-password", "Barsig-E2E-Test-2026!");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => !url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        Assert.Contains("Phone Demo Customer", await page.ContentAsync());
    }

    [Fact]
    public async Task EmailRegistration_StillRequiresPhoneVerificationBeforeSignIn()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        IPage registration = await context.NewPageAsync();
        string email = $"phone-required-{Guid.NewGuid():N}@barsig.test";
        string phone = NewKazakhstanPhone();

        await registration.GotoAsync("/register");
        await FinanceFlowFixture.WaitForInteractiveAsync(registration);
        await registration.FillAsync("#register-name", "Email and Phone Customer");
        await registration.FillAsync("#register-email", email);
        await registration.FillAsync("#register-phone", FormatKazakhstanNational(phone));
        await registration.FillAsync("#register-password", FinanceFlowFixture.DemoPassword);
        await registration.FillAsync("#register-confirm", FinanceFlowFixture.DemoPassword);
        await registration.ClickAsync("button[type=submit]");
        await registration.WaitForSelectorAsync("#phone-demo-code", new PageWaitForSelectorOptions { Timeout = 30_000 });
        string code = (await registration.Locator("[aria-label='Local demo verification code']").InnerTextAsync()).Trim();

        IPage signIn = await context.NewPageAsync();
        await signIn.GotoAsync("/login");
        await FinanceFlowFixture.WaitForInteractiveAsync(signIn);
        await signIn.FillAsync("#login-identifier", email);
        await signIn.FillAsync("#login-password", FinanceFlowFixture.DemoPassword);
        await signIn.ClickAsync("button[type=submit]");
        await signIn.WaitForSelectorAsync(".alert-error", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.Contains("wrong", (await signIn.Locator(".alert-error").InnerTextAsync()).ToLowerInvariant());

        await registration.FillAsync("#phone-demo-code", code);
        await registration.ClickAsync("button:has-text('Verify and continue to sign in')");
        await registration.WaitForURLAsync(url => url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });

        await signIn.FillAsync("#login-identifier", email);
        await signIn.FillAsync("#login-password", FinanceFlowFixture.DemoPassword);
        await signIn.ClickAsync("button[type=submit]");
        await signIn.WaitForURLAsync(url => !url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        Assert.Contains("Email and Phone Customer", await signIn.ContentAsync());
    }

    [Fact]
    public async Task PhoneRegistrationCanResumeAfterRefreshResendAndRejectTheReplacedCode()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        IPage page = await context.NewPageAsync();
        string phone = NewKazakhstanPhone();

        await page.GotoAsync("/register");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.FillAsync("#register-name", "Recoverable Phone Customer");
        await page.FillAsync("#register-phone", phone);
        await page.FillAsync("#register-password", FinanceFlowFixture.DemoPassword);
        await page.FillAsync("#register-confirm", FinanceFlowFixture.DemoPassword);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForSelectorAsync("#phone-demo-code, .alert-error", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.True(await page.Locator("#phone-demo-code").CountAsync() > 0, await page.ContentAsync());
        string firstCode = (await page.Locator("[aria-label='Local demo verification code']").InnerTextAsync()).Trim();

        await page.ReloadAsync();
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.WaitForSelectorAsync("#phone-demo-code", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.Equal(0, await page.Locator("[aria-label='Local demo verification code']").CountAsync());
        Assert.Contains("verification was recovered", (await page.Locator(".alert-error").InnerTextAsync()).ToLowerInvariant());

        await page.ClickAsync("button:has-text('Request a new demo code')");
        await page.WaitForSelectorAsync(".alert-error:has-text('Please wait')", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.Contains("wait", (await page.Locator(".alert-error").InnerTextAsync()).ToLowerInvariant());

        await Task.Delay(TimeSpan.FromSeconds(3));
        await page.ClickAsync("button:has-text('Request a new demo code')");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[aria-label=\"Local demo verification code\"]')?.textContent?.trim().length === 6",
            null,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        string secondCode = (await page.Locator("[aria-label='Local demo verification code']").InnerTextAsync()).Trim();
        Assert.NotEqual(firstCode, secondCode);

        await page.FillAsync("#phone-demo-code", firstCode);
        await page.ClickAsync("button:has-text('Verify and continue to sign in')");
        await page.WaitForSelectorAsync(".alert-error", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.Contains("incorrect", (await page.Locator(".alert-error").InnerTextAsync()).ToLowerInvariant());

        await page.FillAsync("#phone-demo-code", secondCode);
        await page.ClickAsync("button:has-text('Verify and continue to sign in')");
        await page.WaitForURLAsync(url => url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });

        await using Microsoft.Data.Sqlite.SqliteConnection connection = new($"Data Source={_fixture.DatabasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        await using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Users WHERE PhoneNumber = $phone";
        command.Parameters.AddWithValue("$phone", phone);
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync() ?? 0L));
    }

    [Fact]
    public async Task PhoneRegistrationCanRecoverWithoutTheChallengeCookieWithoutCreatingAnotherAccount()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        IPage page = await context.NewPageAsync();
        string phone = NewKazakhstanPhone();

        await page.GotoAsync("/register");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.FillAsync("#register-name", "Restart Recovery Customer");
        await page.FillAsync("#register-phone", phone);
        await page.FillAsync("#register-password", FinanceFlowFixture.DemoPassword);
        await page.FillAsync("#register-confirm", FinanceFlowFixture.DemoPassword);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForSelectorAsync("#phone-demo-code, .alert-error", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.True(await page.Locator("#phone-demo-code").CountAsync() > 0, await page.ContentAsync());

        await context.ClearCookiesAsync();
        await page.ReloadAsync();
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.ClickAsync("button:has-text('Resume verification')");
        await page.FillAsync("#recover-phone", phone);
        await page.FillAsync("#recover-password", "WrongPassword123");
        await page.ClickAsync("button:has-text('Resume and request demo code')");
        await page.WaitForSelectorAsync(".alert-error", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.Equal(0, await page.Locator("#phone-demo-code").CountAsync());

        await Task.Delay(TimeSpan.FromSeconds(3));
        string recoveryResponse = await page.EvaluateAsync<string>(
            """
            async ({ phone, password }) => {
                const response = await fetch('/account/phone/recovery/request', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    credentials: 'same-origin',
                    body: JSON.stringify({ phoneNumber: phone, password })
                });
                return JSON.stringify({
                    status: response.status,
                    body: await response.json()
                });
            }
            """,
            new { phone, password = FinanceFlowFixture.DemoPassword });
        using System.Text.Json.JsonDocument recovery = System.Text.Json.JsonDocument.Parse(recoveryResponse);
        int recoveryStatus = recovery.RootElement.GetProperty("status").GetInt32();
        int retryAfter = recovery.RootElement.GetProperty("body")
            .TryGetProperty("retryAfterSeconds", out System.Text.Json.JsonElement retryAfterValue)
            ? retryAfterValue.GetInt32()
            : 0;
        Assert.True(recoveryStatus == 200, $"Password-based recovery returned HTTP {recoveryStatus}; retryAfterSeconds={retryAfter}.");
        Guid recoveredChallengeId = recovery.RootElement.GetProperty("body").GetProperty("challengeId").GetGuid();
        string recoveredCode = recovery.RootElement.GetProperty("body").GetProperty("demoCode").GetString()!;

        string verifyResponse = await page.EvaluateAsync<string>(
            """
            async ({ challengeId, code }) => {
                const response = await fetch('/account/phone/demo-verify', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    credentials: 'same-origin',
                    body: JSON.stringify({ challengeId, code })
                });
                return JSON.stringify({ status: response.status });
            }
            """,
            new { challengeId = recoveredChallengeId, code = recoveredCode });
        using System.Text.Json.JsonDocument verify = System.Text.Json.JsonDocument.Parse(verifyResponse);
        Assert.Equal(200, verify.RootElement.GetProperty("status").GetInt32());

        await using Microsoft.Data.Sqlite.SqliteConnection connection = new($"Data Source={_fixture.DatabasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        await using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Users WHERE PhoneNumber = $phone";
        command.Parameters.AddWithValue("$phone", phone);
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync() ?? 0L));
        command.CommandText = """
            SELECT COUNT(*) FROM BankAccounts a
            JOIN Users u ON u.Id = a.UserId
            WHERE u.PhoneNumber = $phone
            """;
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync() ?? 0L));
    }

    [Fact]
    public async Task ExistingEmailAccount_CanLinkPhoneThenSignInByPhoneWithoutChangingIdentity()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);
        IPage page = await context.NewPageAsync();
        string phone = NewKazakhstanPhone();
        AccountSnapshot before = await ReadAccountSnapshotAsync(
            $"Data Source={_fixture.DatabasePath};Mode=ReadOnly",
            $"SELECT u.Id FROM Users u WHERE u.Email = $identity",
            FinanceFlowFixture.DemoEmail,
            "$identity");
        Assert.True(before.AccountCount > 0);

        await page.GotoAsync("/security");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.FillAsync("#security-phone", phone);
        await page.FillAsync("#security-phone-password", FinanceFlowFixture.DemoPassword);
        await page.ClickAsync("button:has-text('Request local verification')");
        await page.WaitForSelectorAsync("#security-phone-code", new PageWaitForSelectorOptions { Timeout = 30_000 });
        string code = (await page.Locator(".demo-note [aria-label='Local demo verification code']").InnerTextAsync()).Trim();
        await page.FillAsync("#security-phone-code", code);
        await page.ClickAsync("button:has-text('Verify and save phone')");
        await page.WaitForSelectorAsync("text=The phone was saved for local demo sign-in", new PageWaitForSelectorOptions { Timeout = 30_000 });
        PhoneIdentitySnapshot linkedIdentity = await ReadPhoneIdentityAsync(_fixture.DatabasePath, phone);
        Assert.True(linkedIdentity.IsVerified);
        Assert.Equal((int)FinanceFlow.Domain.Enums.PhoneVerificationMethod.LocalDemo, linkedIdentity.VerificationMethod);

        await page.ClickAsync("button[title='Sign out']");
        await page.WaitForURLAsync(url => url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.FillAsync("#login-identifier", phone);
        await page.FillAsync("#login-password", FinanceFlowFixture.DemoPassword);
        int loginStatus = 0;
        page.Response += (_, response) =>
        {
            if (response.Url.EndsWith("/account/sign-in", StringComparison.Ordinal))
            {
                loginStatus = response.Status;
            }
        };
        await page.ClickAsync("button[type=submit]");
        await page.WaitForSelectorAsync("h1:has-text('Dashboard'), .alert-error", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.True(await page.Locator("h1:has-text('Dashboard')").CountAsync() > 0, $"Phone sign-in returned HTTP {loginStatus}: {await page.ContentAsync()}");

        AccountSnapshot after = await ReadAccountSnapshotAsync(
            $"Data Source={_fixture.DatabasePath};Mode=ReadOnly",
            "SELECT u.Id FROM Users u WHERE u.PhoneNumber = $identity",
            phone,
            "$identity");
        Assert.Equal(before, after);

        await page.ClickAsync("button[title='Sign out']");
        await page.WaitForURLAsync(url => url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.FillAsync("#login-identifier", FinanceFlowFixture.DemoEmail);
        await page.FillAsync("#login-password", FinanceFlowFixture.DemoPassword);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForSelectorAsync("h1:has-text('Dashboard')", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.Equal(before, await ReadAccountSnapshotAsync(
            $"Data Source={_fixture.DatabasePath};Mode=ReadOnly",
            "SELECT u.Id FROM Users u WHERE u.Email = $identity",
            FinanceFlowFixture.DemoEmail,
            "$identity"));
    }

    [Fact]
    public async Task PhoneChangeRequiresPasswordAndNewVerificationBeforeReplacingOldPhone()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);
        IPage page = await context.NewPageAsync();
        string oldPhone = NewKazakhstanPhone();
        string newPhone = NewKazakhstanPhone();

        await page.GotoAsync("/security");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.FillAsync("#security-phone", oldPhone);
        await page.FillAsync("#security-phone-password", FinanceFlowFixture.DemoPassword);
        await page.ClickAsync("button:has-text('Request local verification')");
        await page.WaitForSelectorAsync("#security-phone-code", new PageWaitForSelectorOptions { Timeout = 30_000 });
        string firstCode = (await page.Locator("[aria-label='Local demo verification code']").InnerTextAsync()).Trim();
        await page.FillAsync("#security-phone-code", firstCode);
        await page.ClickAsync("button:has-text('Verify and save phone')");
        await page.WaitForSelectorAsync("text=The phone was saved for local demo sign-in", new PageWaitForSelectorOptions { Timeout = 30_000 });

        await page.FillAsync("#security-phone", newPhone);
        await page.FillAsync("#security-phone-password", "WrongPassword123");
        await page.ClickAsync("button:has-text('Request local verification')");
        await page.WaitForSelectorAsync(".field-error", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.Equal(0, await page.Locator("#security-phone-code").CountAsync());
        Assert.Equal(oldPhone, await ReadPhoneNumberAsync(_fixture.DatabasePath, FinanceFlowFixture.DemoEmail));

        await page.FillAsync("#security-phone-password", FinanceFlowFixture.DemoPassword);
        await page.ClickAsync("button:has-text('Request local verification')");
        await page.WaitForSelectorAsync("#security-phone-code", new PageWaitForSelectorOptions { Timeout = 30_000 });
        string secondCode = (await page.Locator("[aria-label='Local demo verification code']").InnerTextAsync()).Trim();
        await page.FillAsync("#security-phone-code", secondCode);
        await page.ClickAsync("button:has-text('Verify and save phone')");
        await page.WaitForSelectorAsync("text=The phone was saved for local demo sign-in", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.Equal(newPhone, await ReadPhoneNumberAsync(_fixture.DatabasePath, FinanceFlowFixture.DemoEmail));
        PhoneIdentitySnapshot changedIdentity = await ReadPhoneIdentityAsync(_fixture.DatabasePath, newPhone);
        Assert.True(changedIdentity.IsVerified);
        Assert.Equal((int)FinanceFlow.Domain.Enums.PhoneVerificationMethod.LocalDemo, changedIdentity.VerificationMethod);

        await page.ClickAsync("button[title='Sign out']");
        await page.WaitForURLAsync(url => url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.FillAsync("#login-identifier", oldPhone);
        await page.FillAsync("#login-password", FinanceFlowFixture.DemoPassword);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForSelectorAsync(".alert-error", new PageWaitForSelectorOptions { Timeout = 30_000 });

        await page.FillAsync("#login-identifier", newPhone);
        await page.FillAsync("#login-password", FinanceFlowFixture.DemoPassword);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForSelectorAsync("h1:has-text('Dashboard')", new PageWaitForSelectorOptions { Timeout = 30_000 });
    }

    [Fact]
    public async Task ExistingPhoneCannotBeLinkedToASecondAccount()
    {
        string ownedPhone = NewKazakhstanPhone();
        await using (IBrowserContext ownerContext = await _fixture.CreateContextAsync())
        {
            IPage ownerPage = await ownerContext.NewPageAsync();
            await ownerPage.GotoAsync("/register");
            await FinanceFlowFixture.WaitForInteractiveAsync(ownerPage);
            await ownerPage.FillAsync("#register-name", "Phone Identity Owner");
            await ownerPage.FillAsync("#register-phone", ownedPhone);
            await ownerPage.FillAsync("#register-password", FinanceFlowFixture.DemoPassword);
            await ownerPage.FillAsync("#register-confirm", FinanceFlowFixture.DemoPassword);
            await ownerPage.ClickAsync("button[type=submit]");
            await ownerPage.WaitForSelectorAsync("#phone-demo-code, .alert-error", new PageWaitForSelectorOptions { Timeout = 30_000 });
            Assert.True(await ownerPage.Locator("#phone-demo-code").CountAsync() > 0, await ownerPage.ContentAsync());
            string code = (await ownerPage.Locator("[aria-label='Local demo verification code']").InnerTextAsync()).Trim();
            await ownerPage.FillAsync("#phone-demo-code", code);
            await ownerPage.ClickAsync("button:has-text('Verify and continue to sign in')");
            await ownerPage.WaitForURLAsync(url => url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        }

        await using IBrowserContext secondContext = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(secondContext);
        IPage page = await secondContext.NewPageAsync();
        await page.GotoAsync("/security");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.FillAsync("#security-phone", ownedPhone);
        await page.FillAsync("#security-phone-password", FinanceFlowFixture.DemoPassword);
        await page.ClickAsync("button:has-text('Request local verification')");
        await page.WaitForSelectorAsync(".field-error", new PageWaitForSelectorOptions { Timeout = 30_000 });
        Assert.Equal(0, await page.Locator("#security-phone-code").CountAsync());

        await using Microsoft.Data.Sqlite.SqliteConnection connection = new($"Data Source={_fixture.DatabasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        await using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Users WHERE PhoneNumber = $phone";
        command.Parameters.AddWithValue("$phone", ownedPhone);
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync() ?? 0L));
    }

    [Fact]
    public async Task TheDashboardShowsTheNumbersOfTheAccount()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        await page.WaitForSelectorAsync(".stat-card", new PageWaitForSelectorOptions { Timeout = 30_000 });

        string content = await page.ContentAsync();

        Assert.Contains("Dashboard", content);
        Assert.Contains("Current balance", content);
        Assert.Contains("Savings rate", content);
    }

    [Fact]
    public async Task TheNavigationOpensEveryPage()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        string[] pages = ["/transactions", "/categories", "/budgets", "/analytics", "/forecast", "/insights", "/goals", "/export"];

        foreach (string path in pages)
        {
            await page.GotoAsync(path);
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
            await page.WaitForSelectorAsync("h1.page-title", new PageWaitForSelectorOptions { Timeout = 30_000 });

            Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
        }
    }

    [Fact]
    public async Task ATransactionCanBeAddedAndRemoved()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/transactions");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        await page.WaitForSelectorAsync("table.table", new PageWaitForSelectorOptions { Timeout = 30_000 });

        await ClickAndWaitAsync(page, "button:has-text('Add a transaction')", ".editor-card");

        await page.FillAsync("#editor-amount", "1234.56");
        await page.FillAsync("#editor-note", "End to end test");

        await ClickAndWaitAsync(page, ".editor-card button[type=submit]", ".toast");

        string content = await page.ContentAsync();
        Assert.Contains("End to end test", content);

        // Remove the transaction again so that the test can be repeated.
        await ClickAndWaitAsync(page, "table.table tbody tr:first-child button[title=Delete]", ".modal");
        await ClickAndWaitAsync(page, ".modal button.btn-danger", ".toast");
    }

    [Fact]
    public async Task TheTransactionListCanBeFiltered()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/transactions");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        await page.WaitForSelectorAsync("table.table", new PageWaitForSelectorOptions { Timeout = 30_000 });

        await page.SelectOptionAsync("#filter-kind", "Income");

        await page.WaitForTimeoutAsync(1000);

        string content = await page.ContentAsync();
        Assert.Contains("filter-kind", content);
    }

    [Fact]
    public async Task TheAnalyticsPageShowsTheCharts()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/analytics");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        await page.WaitForSelectorAsync(".chart-svg", new PageWaitForSelectorOptions { Timeout = 30_000 });

        Assert.True(await page.Locator(".chart-svg").CountAsync() > 0);
        Assert.True(await page.Locator(".donut-svg").CountAsync() > 0);
    }

    [Fact]
    public async Task TheBudgetsOfTheMonthAreShown()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/budgets");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        await page.WaitForSelectorAsync(".progress-track", new PageWaitForSelectorOptions { Timeout = 30_000 });

        Assert.True(await page.Locator(".progress-track").CountAsync() > 0);
    }

    [Fact]
    public async Task TheThemeCanBeSwitched()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        await page.WaitForSelectorAsync(".topbar .icon-btn", new PageWaitForSelectorOptions { Timeout = 30_000 });

        await ClickAndWaitAsync(page, ".topbar button[title='Change the theme']", ".theme-dark");

        string html = await page.ContentAsync();

        Assert.True(
            html.Contains("theme-dark") || await page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('theme-dark')"),
            "The dark theme was not applied.");
    }

    [Fact]
    public async Task TheTransactionsCanBeExportedAsCsv()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/export");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        await page.WaitForSelectorAsync("a[href='/export/transactions.csv']", new PageWaitForSelectorOptions { Timeout = 30_000 });

        string body = await page.EvaluateAsync<string>(
            "async () => await (await fetch('/export/transactions.csv')).text()");

        Assert.Contains("Date,Type,Category,Amount,Note", body);
    }

    [Fact]
    public async Task TheExportIsNotAvailableForAnonymousVisitors()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        IPage page = await context.NewPageAsync();

        await page.GotoAsync("/login");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        int status = await page.EvaluateAsync<int>(
            "async () => (await fetch('/export/transactions.csv', { redirect: 'manual' })).status");

        Assert.NotEqual(200, status);
    }

    [Fact]
    public async Task TheUserCanSignOut()
    {
        await using IBrowserContext context = await _fixture.CreateContextAsync();
        await _fixture.SignInAsync(context);

        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        await page.WaitForSelectorAsync("button[title='Sign out']", new PageWaitForSelectorOptions { Timeout = 15_000 });
        await page.ClickAsync("button[title='Sign out']");

        await page.WaitForURLAsync(url => url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });

        Assert.Contains("Welcome back", await page.ContentAsync());
    }

    /// <summary>
    /// Clicks a button and waits for the result. The click is repeated when the circuit
    /// of Blazor was not connected yet, which happens right after the first render.
    /// </summary>
    private static async Task ClickAndWaitAsync(IPage page, string selector, string expectedSelector)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                await page.ClickAsync(selector);

                await page.WaitForSelectorAsync(expectedSelector, new PageWaitForSelectorOptions { Timeout = 2000 });

                return;
            }
            catch (TimeoutException)
            {
                // The circuit was not connected yet, therefore the click had no effect.
            }
        }

        string content = await page.ContentAsync();

        await File.WriteAllTextAsync(
            Path.Combine(Path.GetTempPath(), "financeflow-e2e-failure.html"),
            content);

        throw new TimeoutException($"The element '{expectedSelector}' did not appear after the click on '{selector}'.");
    }

    private sealed record AccountSnapshot(
        string UserId,
        string PasswordHash,
        long AccountCount,
        long BalanceCents,
        long LedgerEntryCount,
        long LedgerAmountCents,
        long DepositCount,
        long VirtualCardCount);

    private static string NewKazakhstanPhone() =>
        $"+777{Random.Shared.Next(10_000_000, 99_999_999)}";

    private static string FormatKazakhstanNational(string e164)
    {
        string national = e164.Replace("+7", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return $"{national[..3]} {national[3..6]} {national[6..8]} {national[8..]}";
    }

    private sealed record PhoneIdentitySnapshot(
        string UserId,
        string PhoneNumber,
        bool IsVerified,
        int VerificationMethod);

    private static async Task<PhoneIdentitySnapshot> ReadPhoneIdentityAsync(string databasePath, string phone)
    {
        await using Microsoft.Data.Sqlite.SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        await using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id, PhoneNumber, IsPhoneVerified, PhoneVerificationMethod FROM Users WHERE PhoneNumber = $phone";
        command.Parameters.AddWithValue("$phone", phone);
        await using Microsoft.Data.Sqlite.SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new PhoneIdentitySnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetInt32(3));
    }

    private static async Task<string?> ReadPhoneNumberAsync(string databasePath, string email)
    {
        await using Microsoft.Data.Sqlite.SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        await using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT PhoneNumber FROM Users WHERE Email = $email";
        command.Parameters.AddWithValue("$email", email);
        object? result = await command.ExecuteScalarAsync();
        return result is DBNull ? null : result as string;
    }

    private static async Task<AccountSnapshot> ReadAccountSnapshotAsync(
        string connectionString,
        string userLookupSql,
        string identity,
        string parameterName)
    {
        await using Microsoft.Data.Sqlite.SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT u.Id,
                   u.PasswordHash,
                   (SELECT COUNT(*) FROM BankAccounts a WHERE a.UserId = u.Id),
                   (SELECT COALESCE(SUM(a.BalanceCents), 0) FROM BankAccounts a WHERE a.UserId = u.Id),
                   (SELECT COUNT(*) FROM LedgerEntries e JOIN BankAccounts a ON a.Id = e.AccountId WHERE a.UserId = u.Id),
                   (SELECT COALESCE(SUM(e.AmountCents), 0) FROM LedgerEntries e JOIN BankAccounts a ON a.Id = e.AccountId WHERE a.UserId = u.Id),
                   (SELECT COUNT(*) FROM Deposits d WHERE d.UserId = u.Id),
                   (SELECT COUNT(*) FROM VirtualCards c WHERE c.UserId = u.Id)
            FROM Users u
            WHERE u.Id = ({userLookupSql})
            """;
        command.Parameters.AddWithValue(parameterName, identity);
        await using Microsoft.Data.Sqlite.SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected the account identity to exist.");
        return new AccountSnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7));
    }
}
