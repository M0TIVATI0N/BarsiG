using Microsoft.Playwright;
using System.Net;

namespace FinanceFlow.E2ETests;

/// <summary>
/// The primary acceptance scenario of BarsiG, in a real browser:
/// two customers, the reserve of the Demo Bank, a transfer between the customers,
/// a deposit and the balances at the end.
/// </summary>
[Collection(FinanceFlowCollection.Name)]
public sealed class BankingEndToEndTests
{
    private readonly FinanceFlowFixture _fixture;

    /// <summary>Creates the tests.</summary>
    public BankingEndToEndTests(FinanceFlowFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TheAcceptanceScenario_MovesTheVirtualMoneyAsExpected()
    {
        IBrowserContext aliceContext = await _fixture.CreateContextAsync();
        IBrowserContext bobContext = await _fixture.CreateContextAsync();
        IBrowserContext adminContext = await _fixture.CreateContextAsync();

        try
        {
            // 1. Both customers register with verified local-demo phone identities.
            string aliceName = "Alice" + Guid.NewGuid().ToString("N")[..6];
            string bobName = "Bob" + Guid.NewGuid().ToString("N")[..6];
            string alicePhone = $"+7700{Random.Shared.Next(1_000_000, 9_999_999)}";
            string bobPhone = $"+7700{Random.Shared.Next(1_000_000, 9_999_999)}";

            await RegisterPhoneAsync(aliceContext, aliceName, alicePhone);
            await RegisterPhoneAsync(bobContext, bobName, bobPhone);

            await SignInAsync(aliceContext, alicePhone);
            await SignInAsync(bobContext, bobPhone);

            string aliceAccounts = await ReadPageAsync(aliceContext, "/accounts");
            string bobAccounts = await ReadPageAsync(bobContext, "/accounts");

            Assert.Contains("0,00 ₸", aliceAccounts);
            Assert.Contains("0,00 ₸", bobAccounts);

            // 2. The administrator gives 100 000 out of the reserve to Alice.
            await SignInAsync(adminContext, FinanceFlowFixture.AdminEmail, FinanceFlowFixture.AdminPassword);

            IPage adminPage = await OpenPageAsync(adminContext, "/admin");
            await adminPage.WaitForSelectorAsync("#admin-customer", new PageWaitForSelectorOptions { Timeout = 30_000 });

            await adminPage.WaitForFunctionAsync(
                "(name) => Array.from(document.querySelectorAll('#admin-customer option')).some(option => option.textContent.includes(name))",
                aliceName,
                new PageWaitForFunctionOptions { Timeout = 30_000 });
            string? aliceUserId = await adminPage.Locator("#admin-customer").EvaluateAsync<string?>(
                "(select, name) => Array.from(select.options).find(option => option.textContent.includes(name))?.value ?? null",
                aliceName);
            Assert.False(string.IsNullOrWhiteSpace(aliceUserId), "The administrator page must list Alice as a funding recipient.");
            await adminPage.SelectOptionAsync("#admin-customer", aliceUserId!);
            await adminPage.FillAsync("#admin-amount", "100000");
            await adminPage.ClickAsync("button:has-text('Give the money')");

            await adminPage.WaitForTimeoutAsync(4000);

            string adminPage2 = await ReadPageAsync(adminContext, "/admin");
            await File.WriteAllTextAsync(
                Path.Combine(Path.GetTempPath(), "barsig-e2e-admin.html"),
                adminPage2);

            Assert.Contains("900 000,00 ₸", adminPage2);

            // 3. Alice finds Bob by his verified phone and sends 25 000.
            IPage alicePage = await OpenPageAsync(aliceContext, "/transfers");
            await alicePage.WaitForSelectorAsync("#recipient-phone", new PageWaitForSelectorOptions { Timeout = 30_000 });

            await alicePage.FillAsync("#recipient-phone", FormatKazakhstanNational(bobPhone));
            await alicePage.ClickAsync("button:has-text('Find recipient')");
            await alicePage.WaitForSelectorAsync(".recipient-confirmation", new PageWaitForSelectorOptions { Timeout = 15_000 });
            Assert.Contains(bobName, await alicePage.Locator(".recipient-confirmation").InnerTextAsync());
            await alicePage.ClickAsync("button:has-text('Continue')");

            await alicePage.WaitForSelectorAsync("#amount", new PageWaitForSelectorOptions { Timeout = 15_000 });
            await alicePage.FillAsync("#amount", "25000");
            await alicePage.ClickAsync("button:has-text('Review the transfer')");

            await alicePage.WaitForSelectorAsync("button:has-text('Confirm the transfer')", new PageWaitForSelectorOptions { Timeout = 15_000 });
            ILocator passwordStepUp = alicePage.Locator("#password");
            if (await passwordStepUp.IsVisibleAsync())
            {
                await passwordStepUp.FillAsync("Secret123");
            }

            await alicePage.ClickAsync("button:has-text('Confirm the transfer')");
            await alicePage.WaitForSelectorAsync("a:has-text('Download the receipt')", new PageWaitForSelectorOptions { Timeout = 15_000 });

            // 4. Alice moves 30 000 into a deposit.
            IPage depositPage = await OpenPageAsync(aliceContext, "/deposits");
            await depositPage.WaitForSelectorAsync("#deposit-name", new PageWaitForSelectorOptions { Timeout = 30_000 });

            await depositPage.FillAsync("#deposit-name", "Holiday");
            await depositPage.ClickAsync("button:has-text('Open the deposit')");
            await depositPage.WaitForTimeoutAsync(2000);

            await depositPage.ClickAsync(".list-item button:has-text('Manage')");
            await depositPage.WaitForTimeoutAsync(1000);

            await depositPage.FillAsync("#contribute", "30000");
            await depositPage.ClickAsync("button:has-text('Add money')");
            await depositPage.WaitForTimeoutAsync(2000);

            // 5. The balances of the acceptance scenario.
            string aliceFinal = await ReadPageAsync(aliceContext, "/accounts");
            string bobFinal = await ReadPageAsync(bobContext, "/accounts");

            await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "barsig-e2e-alice.html"), aliceFinal);
            await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "barsig-e2e-bob.html"), bobFinal);

            Assert.Contains("45 000,00 ₸", aliceFinal);
            Assert.Contains("30 000,00 ₸", aliceFinal);
            Assert.Contains("25 000,00 ₸", bobFinal);
        }
        finally
        {
            await aliceContext.CloseAsync();
            await bobContext.CloseAsync();
            await adminContext.CloseAsync();
        }
    }

    [Fact]
    public async Task TheLoginPage_ShowsTheIdentityOfBarsiG()
    {
        IBrowserContext context = await _fixture.CreateContextAsync();

        try
        {
            IPage page = await context.NewPageAsync();

            await page.GotoAsync("/login");
            await FinanceFlowFixture.WaitForInteractiveAsync(page);

            string content = await page.ContentAsync();

            Assert.Contains("BarsiG", content);
            Assert.Contains("Digital Banking Simulator", content);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task OrdinaryCustomer_CannotAccessTheAdminPageOrOverviewEndpoint()
    {
        IBrowserContext context = await _fixture.CreateContextAsync();

        try
        {
            string email = await RegisterAsync(context, "RegularCustomer");
            await SignInAsync(context, email);

            IPage page = await OpenPageAsync(context, "/admin");
            string pageContent = await page.ContentAsync();
            Assert.False(
                pageContent.Contains("Give virtual money to a customer", StringComparison.Ordinal),
                $"A regular customer saw the administrator interface at {page.Url}.");

            BrowserContextCookiesResult authCookie = Assert.Single(
                await context.CookiesAsync(),
                cookie => cookie.Name == "BarsiG.Auth");

            using HttpClientHandler handler = new() { AllowAutoRedirect = false };
            using HttpClient client = new(handler);
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/admin/overview");
            request.Headers.Add("Cookie", $"{authCookie.Name}={authCookie.Value}");

            using HttpResponseMessage response = await client.SendAsync(request);
            Assert.False(response.IsSuccessStatusCode);
            Assert.Contains(
                response.StatusCode,
                new[] { HttpStatusCode.Redirect, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized });
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task<string> RegisterAsync(IBrowserContext context, string displayName)
    {
        string email = $"{displayName.ToLowerInvariant()}-{Guid.NewGuid():N}@barsig.test";
        string phone = NewKazakhstanPhone();

        IPage page = await context.NewPageAsync();

        await page.GotoAsync("/register");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.WaitForSelectorAsync("#register-name", new PageWaitForSelectorOptions { Timeout = 30_000 });

        await page.FillAsync("#register-name", displayName);
        await page.FillAsync("#register-email", email);
        await page.FillAsync("#register-phone", FormatKazakhstanNational(phone));
        await page.FillAsync("#register-password", "Secret123");
        await page.FillAsync("#register-confirm", "Secret123");
        await page.ClickAsync("button[type=submit]");

        await page.WaitForSelectorAsync("#phone-demo-code", new PageWaitForSelectorOptions { Timeout = 30_000 });
        string code = (await page.Locator("[aria-label='Local demo verification code']").InnerTextAsync()).Trim();
        await page.FillAsync("#phone-demo-code", code);
        await page.ClickAsync("button:has-text('Verify and continue to sign in')");
        await page.WaitForURLAsync(url => url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        await page.CloseAsync();

        return email;
    }

    private async Task RegisterPhoneAsync(IBrowserContext context, string displayName, string phone)
    {
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("/register");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.FillAsync("#register-name", displayName);
        await page.FillAsync("#register-phone", FormatKazakhstanNational(phone));
        await page.FillAsync("#register-password", "Secret123");
        await page.FillAsync("#register-confirm", "Secret123");
        await page.ClickAsync("button[type=submit]");
        await page.WaitForSelectorAsync("#phone-demo-code", new PageWaitForSelectorOptions { Timeout = 30_000 });

        string code = (await page.Locator("[aria-label='Local demo verification code']").InnerTextAsync()).Trim();
        Assert.Matches("^\\d{6}$", code);
        await page.FillAsync("#phone-demo-code", code);
        await page.ClickAsync("button:has-text('Verify and continue to sign in')");
        await page.WaitForURLAsync(url => url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        await page.CloseAsync();
    }

    private async Task SignInAsync(IBrowserContext context, string email, string password = "Secret123")
    {
        IPage page = await context.NewPageAsync();

        await page.GotoAsync("/login");
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.WaitForSelectorAsync("#login-identifier", new PageWaitForSelectorOptions { Timeout = 30_000 });

        await page.FillAsync("#login-identifier", email);
        await page.FillAsync("#login-password", password);
        await page.ClickAsync("button[type=submit]");

        await page.WaitForURLAsync(url => !url.Contains("/login"), new PageWaitForURLOptions { Timeout = 30_000 });
        await page.CloseAsync();
    }

    private static string NewKazakhstanPhone() =>
        $"+777{Random.Shared.Next(10_000_000, 99_999_999)}";

    private static string FormatKazakhstanNational(string e164)
    {
        string national = e164.Replace("+7", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return $"{national[..3]} {national[3..6]} {national[6..8]} {national[8..]}";
    }

    private static async Task<IPage> OpenPageAsync(IBrowserContext context, string path)
    {
        IPage page = await context.NewPageAsync();

        await page.GotoAsync(path);
        await FinanceFlowFixture.WaitForInteractiveAsync(page);

        return page;
    }

    private static async Task<string> ReadPageAsync(IBrowserContext context, string path)
    {
        IPage page = await context.NewPageAsync();

        await page.GotoAsync(path);
        await FinanceFlowFixture.WaitForInteractiveAsync(page);
        await page.WaitForTimeoutAsync(1500);

        string content = await page.ContentAsync();

        await page.CloseAsync();

        return content;
    }
}
