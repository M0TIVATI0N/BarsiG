# BarsiG — Digital Banking Simulator

> **BarsiG** is a fictional bank for a university simulation. Every amount is virtual money,
> no real bank account is connected and no payment network is reached.
> It is the final project of the course *Introduction to Functional Programming (FP 3222)*
> of the faculty of informatics, teacher **Tsoi Pavel**.

---

## 1. What is inside

| Area | What the customer can do |
|---|---|
| **Authentication** | Register, sign in, sign out, per customer isolation, PBKDF2 hashing, optional authenticator, transaction PIN |
| **Accounts** | One primary account with exactly 0 ₸, savings deposits, the visible account number, the balance from the ledger |
| **Transfers** | Choose a recipient, confirm the details, enter the amount, review, confirm with a PIN, receive a receipt |
| **Demo Bank** | An administrator funds the customers out of the reserve of the simulation |
| **Deposits** | Open a deposit, add money, take the money back, monthly educational interest, history of the interest |
| **Virtual cards** | Fictional cards, only the last four digits, limits, freeze and unfreeze |
| **Receipts** | A PDF receipt of every transaction with the identity of BarsiG |
| **Security** | The real sessions of the server, the history of the sign ins, the termination of a session |
| **Planning** | The features of FinanceFlow: transactions, categories, budgets, goals, analytics, forecast, insights, export |
| **Real time** | SignalR: the balance of the other client changes immediately |

## 2. The most important idea: the money never appears out of nothing

The heart of the project is a **double entry ledger**.

```
BankAccount      the containers of the money (primary, deposit, reserve)
LedgerTransfer   one business transaction (transfer, funding, interest, withdrawal)
LedgerEntry      one immutable debit or credit line of a transaction
```

The rules that the code keeps, and that the tests prove:

1. Every committed transaction has exactly **two** ledger entries with the same amount.
2. The sum of all entries of all accounts is **exactly zero**: the money is moved, never created.
3. The balance of an account is always the sum of its entries. The stored balance is only a cache
   that is written in the same database transaction as the entries.
4. The ledger rows are never changed and never deleted. A correction is a compensating transaction.
5. A customer reads only the accounts he owns.

The reserve of the Demo Bank has a counterpart account that holds the issued money, so even
the very first row of the database satisfies the second rule.

## 3. How to start it

### The web application with the final local database

Start the existing local demo database from PowerShell in the project folder:

```powershell
.\scripts\start-isolated-admin-demo.ps1 -ReuseExisting
```

The server uses `local-demo-admin\financeflow-admin-demo.db` and listens at
<http://127.0.0.1:5198>. On the first run of a fresh copy only, initialize the database
without `-ReuseExisting`; the script asks for the administrator password securely.

### The Windows application

Use the **BarsiG Final** shortcut on the Windows desktop, or run:

```powershell
.\scripts\start-final-desktop.ps1
```

`BarsiG.exe` starts the server **inside its own process** on a free port and shows the
interface in a window of Microsoft Edge WebView2. The user never has to type an address.
Closing the window stops the server, no process is left behind. The launcher publishes the
desktop app when it is missing and always points it to the same primary local database.

### Demonstration accounts and the administrator

| Role | How to sign in |
|---|---|
| Customer | Register in the app with a Kazakhstan mobile number and complete the required LocalDemo verification. This is an educational code; no SMS is sent. |
| Administrator of the Demo Bank | Sign in with `admintime@barsig.local` and the password set locally when the primary demo database was initialized. The password is not stored in the project. |

## 4. The demonstration of the acceptance scenario

1. Start the backend, open the Windows application and the web application in a browser.
2. Register **User A** and **User B**. For phone-only registration, enter each displayed Development LocalDemo code; it does not send SMS. Both primary accounts show exactly **0 ₸**.
3. Sign in as the administrator, open *Demo Bank → Administration* and give **100 000 ₸** to User A.
   The reserve now holds **900 000 ₸**.
4. Sign in as User A, find User B by the exact verified international phone number, review the masked recipient details and send **25 000 ₸**.
   User B sees the new balance immediately, the SignalR channel pushes it.
5. Open *Deposits*, create a deposit and move **30 000 ₸** into it.
   The primary account holds **45 000 ₸**, the deposit holds **30 000 ₸**.
6. Download the receipt of the transfer as a PDF.
7. Create a virtual card and freeze it, then unfreeze it again.
8. Open *Security*: the sessions, the history of the sign ins, the transaction PIN, the authenticator.
9. Restart the backend. Every balance, every deposit and every entry of the ledger is still there.

## 5. The structure of the solution

```
IFP_Final_Project/
├── FinanceFlow.sln
├── Directory.Build.props            the common settings
├── Directory.Packages.props         the central management of the versions
├── docs/                            the documentation, see the list at the end
├── src/
│   ├── FinanceFlow.Domain/          the pure business core
│   │   ├── Common/                  Result<T>, Money, Totp, the lazy operators
│   │   ├── Banking/                 the accounts, the ledger, the deposits, the cards
│   │   ├── Entities/                the records of the personal finance
│   │   ├── Analytics/               the calculators, the forecast, the insights
│   │   └── Validation/              the pure validators
│   ├── FinanceFlow.Application/     the use cases
│   │   ├── Abstractions/            the ports of the repositories
│   │   ├── Banking/                 the services of the bank
│   │   └── Services/                the services of the personal finance
│   ├── FinanceFlow.Infrastructure/  SQLite, the ledger, SignalR, the workers, the PDF
│   ├── FinanceFlow.Web/             the Blazor application, the server of the simulation
│   └── FinanceFlow.Desktop/         the Windows application with WebView2
└── tests/
    ├── FinanceFlow.UnitTests/       the tests of the domain and the application layer
    ├── FinanceFlow.ComponentTests/  the tests of the components
    ├── FinanceFlow.IntegrationTests/ the tests against a real SQLite database
    └── FinanceFlow.E2ETests/        the tests in a real browser
```

## 6. The functional programming of the project

| Idea | Where it is used |
|---|---|
| **Immutable records** | Every entity, every read model, every stage of a pipeline |
| **Pure functions** | `BankingMath`, `DepositInterestCalculator`, `AnalyticsCalculator`, `Totp` |
| **`Result<T>` instead of exceptions** | Every expected problem is a value, the caller can never forget it |
| **Type state pipeline** | `TransactionDraft` → `ValidatedTransaction` → `Transaction` |
| **LINQ as a pipeline** | The reports, the aggregation of the ledger, the analytics |
| **Lazy sequences** | The own operators `Filter`, `Transform`, `Fold`, `Head` |
| **Function composition** | `Function.Compose`, `Function.Flip` |
| **Pattern matching** | `switch` expressions over the results, the kinds and the states |
| **Total money type** | `Money` keeps the amount in cents, there is no floating point error |
| **Dependency inversion** | Every side effect (clock, hashing, notifications, the ledger) is an interface |

The two rules of the bank are pure functions, they can be proved without a database:

```csharp
// Domain/Banking/BankingMath.cs
public static Money CalculateMonthlyInterest(Money principal, decimal annualRatePercent)
{
    if (principal.Cents <= 0 || annualRatePercent <= 0m)
    {
        return Money.Zero;
    }

    decimal yearlyInterest = principal.ToDecimal() * (annualRatePercent / 100m);
    decimal monthlyInterest = yearlyInterest / MonthsPerYear;

    return Money.FromDecimal(monthlyInterest);
}
```

```csharp
// Domain/Banking/BankingMath.cs — the conservation of the money
public static long SignedCents(LedgerEntryDirection direction, Money amount) =>
    direction == LedgerEntryDirection.Debit ? -amount.Cents : amount.Cents;
```

## 7. The security of the simulation

* **Server side.** Every rule is checked by the server, never by the interface.
* **Passwords** are hashed with PBKDF2 (100 000 iterations, a random salt per user).
* **The transaction PIN** is hashed with the same function. It is a second secret, **not** a
  second factor of the authentication, and it is never presented as one.
* **The authenticator** is a one time password of RFC 6238, proved with the test vectors of
  the standard.
* **Step up.** A transfer asks for the PIN, or for the password when the session is older
  than five minutes.
* **Throttling.** Five wrong passwords block the account for five minutes, the rate limiter
  of ASP.NET Core protects the sign in and the endpoints that move money.
* **Sessions.** The server writes the sessions into the database, the security page shows the
  real sessions and can terminate them.
* **Audit.** Every sensitive action writes an immutable row into the audit.
* **Anti CSRF.** The endpoints that move money require an antiforgery token, the components of
  the interface call the services inside the server, where a request of another site cannot
  reach them.
* **Cookies** are `HttpOnly`, `SameSite=Lax` and `Secure` when the connection is encrypted.

## 8. The tests

```bash
dotnet test                                      # everything
dotnet test tests/FinanceFlow.UnitTests          # only the unit tests
dotnet test tests/FinanceFlow.IntegrationTests   # the tests against a real database
dotnet test --collect:"XPlat Code Coverage"      # with the coverage report
```

| Project | Kind | What it proves |
|---|---|---|
| `FinanceFlow.UnitTests` | unit | The money, the results, the lazy operators, the validators, the banking rules, the one time password of RFC 6238 |
| `FinanceFlow.ComponentTests` | component | The reusable components, the charts, the modals, the toasts |
| `FinanceFlow.IntegrationTests` | integration | The ledger against a real SQLite file: double entry, conservation, idempotency, concurrency, deposits, isolation |
| `FinanceFlow.E2ETests` | end to end | The real browser: sign in, the pages, the isolation of the accounts, the export, the theme |

## 9. The database

* The final local app uses `local-demo-admin\financeflow-admin-demo.db`.
* The database stays on this computer and is excluded from GitHub because it contains account data and password hashes.
* The launch scripts pass its absolute path to the application so the web and Windows launchers share the same database.
* The amounts are stored as whole cents, the moments as Unix milliseconds.
* Every query of a customer is filtered by the identifier of the user.

New migration:

```bash
dotnet tool restore
dotnet ef migrations add NameOfTheChange -p src/FinanceFlow.Infrastructure -s src/FinanceFlow.Infrastructure
```

## 10. The configuration

The launchers set `ConnectionStrings__FinanceFlow` to the primary local database at runtime.
No database file, admin password, or other local credential is committed to the repository.

The file of the desktop application adds the address of a remote server:

```json
{
  "Desktop": {
    "ServerUrl": "https://barsig.example.com",
    "Environment": "Development"
  }
}
```

### Two computers, one server

The database belongs to the **server**. A second computer is only a client of it.

1. On the first computer the server is started and it listens on the network:

   ```powershell
   $env:ConnectionStrings__FinanceFlow = "Data Source=$((Resolve-Path .\local-demo-admin\financeflow-admin-demo.db).Path)"
   dotnet run --project src/FinanceFlow.Web -- --urls http://192.168.1.10:5099
   ```

2. On the second computer the address is written into the settings of the client:

   ```json
   { "Desktop": { "ServerUrl": "http://192.168.1.10:5099" } }
   ```

3. Or the address is given as the environment variable `BARSIG_SERVER_URL`.

**Use HTTPS for an untrusted network.** Outside the development environment the server
redirects to HTTPS (`UseHsts`, `UseHttpsRedirection`) and the cookies of the authentication
carry the flag `Secure`. For a demonstration in a trusted room the plain HTTP of the example
above is enough, but the address must never be published in the internet.

> The scenario with two physical computers was **not verified** during the development,
> because only one computer was available. The scenario with **two clients on one computer**
> (the Windows application and the browser) was verified: both show the same balances.
> This is written down in `docs/REQUIREMENTS_TRACEABILITY.md`.

## 11. The documentation

| Document | Content |
|---|---|
| [docs/BARSIG_IMPLEMENTATION_PLAN.md](docs/BARSIG_IMPLEMENTATION_PLAN.md) | The plan of the upgrade, the stages, the decisions, the results of the tests |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | The layers, the ledger, the diagrams, the algorithms |
| [docs/DEVELOPER_NOTES.md](docs/DEVELOPER_NOTES.md) | The conventions, how to extend the code |
| [docs/TESTING.md](docs/TESTING.md) | The strategy of the tests, how to run them |
| [docs/REQUIREMENTS_TRACEABILITY.md](docs/REQUIREMENTS_TRACEABILITY.md) | The 20 criteria of the course and where they are fulfilled |
| [docs/FINAL_LOCAL_QA.md](docs/FINAL_LOCAL_QA.md) | Local release verification and acceptance checks |
| [docs/BARSIG_KZ_PHONE_REGISTRATION.md](docs/BARSIG_KZ_PHONE_REGISTRATION.md) | Kazakhstan phone registration and verification |
| [docs/BARSIG_V3_DESIGN_SYSTEM.md](docs/BARSIG_V3_DESIGN_SYSTEM.md) | BarsiG V3 interface design |
| [docs/BARSIG_DEMO_RUN_RESULTS.md](docs/BARSIG_DEMO_RUN_RESULTS.md) | Local demo run results |
| [docs/PARTNER_DEMO_GUIDE.md](docs/PARTNER_DEMO_GUIDE.md) | Demo walkthrough |
| [docs/IFP_CONCEPTS.md](docs/IFP_CONCEPTS.md) | Functional programming concepts used in the project |

## 12. The team

* **Azamat Ziadanov** — the domain and the application layer, the ledger, the security, the unit tests
* **Dauit Khassenov** — the interface, the Windows application, the charts, the end to end tests, the documentation

Course: *Introduction to Functional Programming (FP 3222)*, teacher: *Tsoi Pavel*.
