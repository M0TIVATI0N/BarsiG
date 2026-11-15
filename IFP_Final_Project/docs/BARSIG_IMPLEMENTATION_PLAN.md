# BarsiG — Digital Banking Simulator: implementation plan

> Durable technical notes of the upgrade of **FinanceFlow** (personal finance manager) into
> **BarsiG** (virtual banking simulator). One authoritative backend, one database, several clients.
>
> Course: *Introduction to Functional Programming (FP 3222)*, instructor: *Tsoi Pavel*.
> Team: **Azamat Ziadanov**, **Dauit Khassenov**.

---

## 0. Baseline (Stage 0)

| Item | Value |
|---|---|
| Project root | `C:\универ\Introduction to Functional programming  Tsoi Pavel\натуралы(наверное) и Актос\IFP_Final_Project` |
| Solution | `FinanceFlow.sln` (7 projects) |
| SDK | .NET SDK **10.0.400** (also 9.0.308 installed) |
| Runtimes | .NET / ASP.NET Core / WindowsDesktop **8.0.22, 9.0.11, 10.0.11** |
| WebView2 Runtime | **157.0.4341.46** installed (Edge WebView, `Program Files (x86)\Microsoft\EdgeWebView\Application`) |
| Playwright browsers | `chromium-1228`, `chromium-1248`, `chromium_headless_shell-*` cached |
| Backup | `..\backup_2026-11-13\IFP_Final_Project` (164 files, 7.1 MB, incl. `financeflow.db`) |
| Release build | **succeeded, 0 warnings, 0 errors** |
| Unit tests | **94 / 94 passed** |
| Component tests | **25 / 25 passed** |
| E2E tests | see *Stage 9* (baseline run) |

### Brand assets (verified, read-only, never regenerated)

| File | Size | Use |
|---|---|---|
| `BarsiG_App_Icon.ico` | 160 363 B, 32×32 | Windows executable / window / taskbar / shortcuts |
| `BarsiG_App_Icon.png` | 1024×1024 | favicon, web app icons |
| `BarsiG_Logo.png` | 1065×320 | navigation, login, documentation |
| `BarsiG_Dashboard_Banner.png` | 1536×343 | dashboard hero region |

Palette: Midnight `#101820`, Slate `#1C2B35`, Sage `#477C71`, Mist `#D5E0DA`, Off-white `#F4F6F4`.

---

## 1. Architecture decisions

### 1.1 Coexistence with FinanceFlow

The personal-finance domain (income/expense, budgets, analytics, forecast, insights, goals,
export) is **preserved unchanged** and stays a separate functional module. The banking module is
**additive**:

* `Domain/Banking/*` — banking entities, ledger, pure banking math.
* `Application/Banking/*` — banking use cases returning `Result<T>`.
* `Infrastructure/Banking/*` — EF Core persistence, atomic ledger executor, PDF, TOTP.
* `Web/Components/Pages/Banking/*` — the new pages.

**Hard rule:** an old expense record never becomes a banking balance. Personal-finance transactions
and banking ledger entries are stored in different tables and never mix.

### 1.2 The ledger (double entry)

```
BankAccount      — the money containers (primary, deposit, reserve)
LedgerTransfer   — one business transaction (transfer, funding, interest, withdrawal)
LedgerEntry      — one immutable debit/credit line of a transfer
```

Invariants (checked by tests, never violated by the UI):

1. **I1 — Double entry.** Every committed `LedgerTransfer` has exactly two `LedgerEntry` rows
   (debit + credit) with the same absolute amount.
2. **I2 — Conservation.** The sum of all entry amounts over all accounts is exactly `0`.
   Money is only moved, never created or destroyed.
3. **I3 — Balance agreement.** For every account: `balance == Σ signed entries`. The stored
   balance is a cache that is always written in the same transaction as the entries;
   `LedgerIntegrityChecker` recomputes it and is used in tests and in the admin "integrity" view.
4. **I4 — Immutability.** `LedgerEntry` and `LedgerTransfer` rows are insert-only; corrections are
   compensating transfers, never edits or deletes.
5. **I5 — Ownership.** A user may read only the accounts they own (or the reserve, for admins).

### 1.3 Atomicity and concurrency

* One SQLite database on the **server**. All clients (web browser, Windows desktop) talk to it.
* Every money movement runs inside a single EF Core transaction. `Microsoft.Data.Sqlite` starts
  transactions in `IMMEDIATE` mode, so writers are serialized by the database.
* An optimistic concurrency token (`Version` integer, `IsConcurrencyToken`) protects the account
  rows against lost updates.
* A process-wide `SemaphoreSlim` (one per server process) additionally serializes ledger writes
  inside the process, so two simultaneous transfers of the same account cannot interleave.
* **Failure atomicity:** a failed transfer leaves *no* debit, *no* credit and *no* balance change.

### 1.4 Idempotency and limits

* Every transfer request carries an `IdempotencyKey` (client generated GUID).
  Unique index on `(RequestingUserId, IdempotencyKey)`; a repeated key returns the original
  transaction instead of executing it twice.
* Limits from `BankingOptions` (configuration): minimum, maximum per transfer, maximum per day.
* Rejected: negative, zero, non-positive amounts, self-transfer, unknown recipient, unowned
  account, insufficient funds, duplicate key, limit exceeded, closed/frozen account.

### 1.5 Authentication and security

* ASP.NET Core cookie authentication, `SameSite=Lax`, `HttpOnly`, `SecurePolicy=Always` when HTTPS.
* New claims: `role` (`User` | `Admin`), `sid` (server-side session id), `auth_time`
  (Unix time of the last successful password check).
* **Server-side sessions** (`UserSessions` table): id, user, created, last seen, IP, user agent,
  revoked. The Security page shows *real* sessions and can terminate them.
* **Login history** (`LoginEvents`) and **security audit** (`SecurityAuditLogs`) are persisted.
* **Step-up authentication** for sensitive operations (transfers, admin funding, PIN change):
  the session must be younger than `StepUpWindow` (default 5 min) **or** the user re-enters the
  transaction PIN. The PIN is stored with the same PBKDF2 hasher as the password — never in plain
  text. The PIN is explicitly *not* claimed to be multi-factor authentication.
* **TOTP 2FA** (RFC 6238, HMAC-SHA1) implemented as a pure function in `Domain/Banking/Totp.cs`
  and covered by the official RFC test vectors. Optional per user.
* **Login throttling**: failed attempts per account and per IP are counted; the account is
  temporarily locked. ASP.NET Core rate limiting protects the sign-in and transfer endpoints.
* **Audit**: every sensitive action writes an immutable audit row (actor, action, target, result).

### 1.6 Demo Bank administration

* A dedicated `Admin` role. The administrator account is created from configuration
  (`Banking:Admin:Email` / `Banking:Admin:Password` or the environment variables
  `BARSIG_ADMIN_EMAIL` / `BARSIG_ADMIN_PASSWORD`). **No password is hardcoded in the source.**
* The bank holds a **reserve** account (a `BankAccount` of type `Reserve`, not owned by a user)
  with a configurable initial amount (default **1 000 000 ₸**).
* The administrator can only *move* money out of the reserve into a user's primary account.
  There is **no** balance editing anywhere: every movement is a ledger transfer, so the reserve
  decreases by exactly the amount the user receives (invariant I2).
* All administrative endpoints require a server-side role check (`RequireAuthorization("Admin")`)
  **and** an explicit ownership check inside the service.

### 1.7 Deposits

* A deposit is a `BankAccount` of type `Deposit` plus a `Deposits` row (rate, term, status,
  `LastAccruedMonth`).
* Contributions and withdrawals are ledger transfers between the primary account and the deposit
  account, so they are atomic and visible in the history.
* **Interest** (educational, explicitly labelled): `interest = principal × rate / 12`, rounded
  half-to-even to whole cents, calculated by the pure function
  `DepositInterestCalculator.CalculateMonthlyInterest`. It is funded from the reserve, therefore
  the total supply stays constant.
* **No double accrual**: the deposit stores `LastAccruedMonth`; the accrual job skips months that
  are already recorded (`DepositInterestAccruals` has a unique index on
  `(DepositId, PeriodMonth)`).
* **Time simulation** for the defence: `Development` only, an admin endpoint moves the simulated
  clock forward by N days/months (`IClock` implementation with an offset) and the accrual job runs
  for the simulated months.

### 1.8 Windows desktop application

`src/FinanceFlow.Desktop` — WPF (`net10.0-windows`) + Microsoft Edge WebView2.

* **Why WPF + WebView2:** it reuses the existing Blazor components unchanged (the requirement)
  and is fully supported on Windows 10/11. Rewriting the whole project into MAUI was rejected:
  it would duplicate the UI, break the existing component tests and add no functional value.
* **In-process hosting:** the desktop application starts the *same* ASP.NET Core application
  in-process on an automatically chosen free port and navigates the WebView2 control to it.
  One process, one window, no orphan processes — closing the window stops the server.
  (The web application does **not** depend on the desktop application.)
* **Remote mode:** `Desktop:ServerUrl` in `appsettings.json` (or the `BARSIG_SERVER_URL`
  environment variable) points the desktop client to a server on another computer. HTTPS is
  required for untrusted networks and is documented.
* WebView2 is configured securely: developer tools off, default context menu off, status bar off,
  a dedicated user-data folder, no external navigation.
* `BarsiG.exe` is produced by `dotnet publish src/FinanceFlow.Desktop -c Release` with
  `ApplicationIcon = BarsiG_App_Icon.ico`.

### 1.9 The three approved additional features

| Feature | Design |
|---|---|
| **A. Virtual cards** | `VirtualCards` table, fictional `9999 …` numbers, only the last four digits are ever stored and shown, limits, freeze/unfreeze, ownership checks. No real card credentials. |
| **B. Transfer receipts** | PDF via **QuestPDF** (community licence, non-commercial educational use): BarsiG branding, transaction id, sender/recipient display names, amount and currency, timestamp, status, explicit "virtual simulation" notice. |
| **C. Login history & sessions** | `LoginEvents` + real server-side `UserSessions` with termination. No fake "active sessions". |

Explicitly **not** implemented: QR transfers, bill payments, external bank integrations, real
money, crypto, loans/credit.

### 1.10 New NuGet packages (project-local)

| Package | Version | Why |
|---|---|---|
| `Microsoft.Web.WebView2` | latest stable | WPF WebView2 control for the desktop client |
| `QuestPDF` | latest stable | PDF receipts and the PDF report (non-commercial licence context) |
| `DocumentFormat.OpenXml` | latest stable | PPTX presentation and DOCX report |

All versions are managed centrally in `Directory.Packages.props`. No system-wide installation is
required; the WebView2 **runtime** is already installed.

---

## 2. Stage log

### Stage 0 — Baseline audit, backup, plan ✅

* **Done:** environment audit, brand asset verification, architecture inspection, backup,
  Release build (0 warnings), unit tests 94/94, component tests 25/25, this plan.
* **Files:** `docs/BARSIG_IMPLEMENTATION_PLAN.md`.
* **Blockers:** none.

### Stage 1 — Bank accounts, ledger, data isolation ✅

* **Domain:** `Banking/BankingEntities.cs` (`BankAccount`, `LedgerTransfer`, `LedgerEntry` + 6 enums),
  `Banking/BankingMath.cs` (pure interest, limits, account numbers), `Banking/UserSecurityProfile.cs`.
* **Application:** `Abstractions/BankingRepositories.cs` (6 ports), `Banking/AccountService.cs`,
  `Banking/Dtos/BankingDtos.cs`.
* **Infrastructure:** `Banking/LedgerExecutor.cs` (atomic double entry + `LedgerWriteGate`),
  `Persistence/Configurations/BankingConfigurations.cs`, `Persistence/Repositories/BankingRepositories.cs`,
  `Persistence/Repositories/SecurityProfileRepository.cs`, `Seeding/BankingSeeder.cs` (reserve + issuance
  counterpart, so the total supply of the simulation is **0** from the very first row).
* **Migration:** `20261112213245_BarsiG_Banking` — **purely additive** (4 new tables, no change to the
  existing ones). The old data of FinanceFlow is untouched.
* **Tests:** `tests/FinanceFlow.UnitTests/BankingTests.cs` (20 tests) and the new project
  `tests/FinanceFlow.IntegrationTests` (9 tests against a real SQLite file).
* **Result:** double entry, conservation, idempotency, concurrency and isolation are proved.

### Stage 2 — Secure user-to-user transfers ✅

* `Application/Banking/TransferService.cs`: recipient search, confirmation details, preview with the
  limits of the bank, atomic execution, notification of both sides through SignalR.
* `Application/Banking/BankingOptions.cs` + `Infrastructure/Banking/BankingOptionsFactory.cs`
  (minimum, maximum, daily maximum, interest rate, initial reserve — all from the configuration).
* Endpoints: `/banking/recipients`, `/banking/recipients/resolve`, `/banking/transfers/preview`,
  `/banking/transfers` (with the antiforgery metadata), `/banking/accounts`, `/banking/history`.

### Stage 3 — Demo Bank administration ✅

* `Application/Banking/AdminService.cs`: funding out of the reserve, the overview of the bank and the
  independent check of the ledger.
* `Infrastructure/Seeding/AdminSeeder.cs`: the administrator is created from `BARSIG_ADMIN_EMAIL` and
  `BARSIG_ADMIN_PASSWORD` (or the local configuration). **No password in the source code.**
* The role travels in the cookie, the policy `Admin` is checked by the server on every endpoint.

### Stage 4 — Deposits and interest ✅

* `Domain/Banking/Deposit.cs` (`Deposit`, `DepositInterestAccrual`, `DepositStatus`).
* `Application/Banking/DepositService.cs`: open, contribute, withdraw, monthly interest, history.
* `Infrastructure/Banking/…`: the repository, the `DepositInterestWorker` (every 30 s) and the
  `AdjustableClock`, the clock that the development tools can move forward.
* **No double accrual:** the unique index on `(DepositId, PeriodMonth)` in the database and the
  deterministic idempotency key `interest:{deposit}:{yyyy-MM}` of the ledger executor.
* The interest is funded from the reserve, so the total supply of the simulation stays constant.
* **Migration:** `BarsiG_Deposits` (additive).

### Stage 5 — Security and step-up authorization ✅

* `Domain/Common/Totp.cs`: the one time password of RFC 6238, proved with the vectors of the
  standard (`tests/FinanceFlow.UnitTests/TotpTests.cs`).
* `Domain/Banking/SecurityEntities.cs`: `CustomerSession`, `LoginEvent`, `SecurityAuditLog`.
* `Application/Banking/SecurityService.cs`: sessions, history of the sign ins, the audit, the
  transaction PIN, the authenticator, the confirmation of the sensitive operations.
* `Infrastructure/Banking/LoginThrottle.cs`: five wrong passwords block the account for five minutes.
* The rate limiter of ASP.NET Core protects the sign in (`20 / min`) and the money endpoints
  (`30 / min`).
* The cookie carries the role, the identifier of the session and the moment of the sign in.
* **Migration:** `BarsiG_Security` (additive).

### Stage 6 — Virtual cards, PDF receipts and login history ✅

* `Domain/Banking/VirtualCard.cs` and `Application/Banking/VirtualCardService.cs`: fictional
  cards, only the last four digits are stored, limits, freeze and unfreeze, ownership checks.
* `Application/Banking/ReceiptService.cs` and `Infrastructure/Export/ReceiptPdfRenderer.cs`:
  the PDF receipt with the identity of BarsiG, made with **QuestPDF** (community licence,
  non commercial educational use). The logo is embedded into the assembly.
* The login history and the sessions are the real rows of the database, the security page
  shows them and can terminate a session.
* **Migration:** `BarsiG_VirtualCards` (additive).

### Stage 7 — BarsiG UI/UX and approved visual assets ✅

* The palette of the identity (midnight `#101820`, slate `#1C2B35`, sage `#477C71`,
  mist `#D5E0DA`, off-white `#F4F6F4`) replaced the old colours of the design system,
  in the light theme and in the dark theme.
* The logo in the navigation, on the sign in page and on the registration page, the banner
  in the hero region of the dashboard, the icon as the favicon.
* The navigation is grouped: **Dashboard**, **Banking** (accounts, transfers, deposits, cards),
  **Planning** (transactions, categories, budgets, goals), **Analytics**, **Account**
  (security, export) and **Demo Bank** for the administrator.
* Six new pages: `Accounts`, `Transfers`, `Deposits`, `Cards`, `Security`, `Admin`.

### Stage 8 — Windows desktop application and shared-server connectivity ✅

* `src/FinanceFlow.Desktop`: WPF (`net10.0-windows`) with Microsoft Edge WebView2.
* The server of the simulation runs **inside the desktop process** (`Program.CreateApp` and
  `Program.PrepareAsync` in `src/FinanceFlow.Web/Program.cs` were extracted for that).
  A free port is chosen automatically, the user never types an address.
* Closing the window stops the server, there is no orphan process.
* `BarsiG.exe` is produced by `dotnet publish src/FinanceFlow.Desktop -c Release`, the approved
  ICO is the icon of the executable and of the window.
* The WebView2 is configured safely: no developer tools, no context menu, no status bar,
  no navigation away from the server, a private folder for the profile of the browser.
* A remote server can be used with `Desktop:ServerUrl` or with `BARSIG_SERVER_URL`.
* **Verified:** the published application was started, it listened on a free port of the
  loopback interface and answered with `HTTP 200` on `/login` and on the logo.

### Stage 9 — Regression, integration, cross-client demo and documentation ✅

* **No test of the baseline was removed or weakened.** The 150 tests of FinanceFlow are still
  there, 50 new tests were added.
* New project `tests/FinanceFlow.IntegrationTests`: the ledger against a real SQLite file.
* New end to end test `BankingEndToEndTests.TheAcceptanceScenario_MovesTheVirtualMoneyAsExpected`:
  the whole acceptance scenario in a real browser (two customers, the reserve, the transfer,
  the deposit, the balances).
* **A real defect was found by the new tests:** the end to end fixture started the
  application with the configuration `Debug` while the tests ran with `Release`, so the
  measured result was an old build. The fixture now uses the configuration of the test run.
  The rate limiter of the sign in was also too strict for several clients behind one address,
  it was changed to 60 requests per minute (the protection against a brute force is the
  throttle of the account: five wrong passwords block the account for five minutes).
* The documentation was updated: README, architecture, user guide, testing, traceability,
  the defence guide, the presentation, the plan of the team and the audit report.
* **17 real screenshots** of the running application were made with Playwright
  (`docs/images`), including the new pages of BarsiG.

### Stage 10 — Presentation, report and defence preparation ✅

* **`docs/BarsiG-Presentation.pptx`** — a real presentation with 17 slides, built by the
  program `tools/BarsiG.Presentation` (DocumentFormat.OpenXml). The file was checked with the
  validator of the Open XML SDK: **no problem**.
* `docs/PRESENTATION.md` — the outline of the slides with the plan of the demonstration.
* `docs/DEFENSE_GUIDE.md` — 20 new questions of the examiner about BarsiG, every answer names
  a file and a test.
* `docs/REQUIREMENTS_TRACEABILITY.md` — the table of the 24 new requirements with the evidence.
* `docs/TEAM_WORK_PLAN.md` — the real division of the work between the two students.
* `reports/AUDIT_REPORT.md` — the measured result of the audit of BarsiG.

---

## 3. Verification ledger (filled as the work proceeds)

| Date | Stage | Build | Unit | Component | Integration | E2E | Notes |
|---|---|---|---|---|---|---|---|
| 2026-11-13 | 0 | Release OK, 0 warn | 94/94 | 25/25 | — | 31/31 | baseline of FinanceFlow |
| 2026-11-13 | 1–8 | Release OK, 0 warn | 127/127 | 25/25 | 15/15 | 31/31 | BarsiG, the banking module |
| 2026-11-13 | 9–10 | Release OK, 0 warn | 127/127 | 25/25 | 15/15 | 41/41 | the acceptance scenario and the documentation |

**Total: 208 automated tests, all green.** The build has no warning and no error.

### What was verified by hand

| Check | Result |
|---|---|
| The published `BarsiG.exe` starts, listens on a free port and answers with `HTTP 200` | yes |
| The static files of the interface are served inside the desktop application | yes |
| The presentation is a valid PPTX file | yes, the validator of the Open XML SDK found no problem |
| The migrations are purely additive | yes, four new tables, no changed column |
| The backup of the project and of the database exists | yes, `..\backup_2026-11-13` |

### What was not verified

* The scenario with **two physical computers** was not run, only one computer was available.
  The configuration and the documentation exist (`Desktop:ServerUrl`, the HTTPS redirection,
  the secure cookies). The scenario with **two clients on one computer** was verified.
* The demonstration of the interest was verified by the integration tests and by the button
  of the administration page, not by a recorded video.

---

# The defect of the published Windows application and its repair

> The defect was reported after the first audit: in the published `BarsiG.exe` the pages of the
> sign in and the registration opened, but after sending the form the browser showed
> **HTTP ERROR 400**. This chapter records the diagnosis, the repair and the evidence.

## 1. The exact cause

The chain of the error was measured on the published application, not guessed.

| Step | Measurement | Result |
|---|---|---|
| 1 | `GET /login` on the published application | **200**, the page contains the reference to `blazor.web.js` |
| 2 | `GET /_framework/blazor.web.js` on the published application | **404 — the file was missing** |
| 3 | `POST /login` as an ordinary form, without the antiforgery token | **400** |

**The cause:** the file `wwwroot/_framework/blazor.web.js` — the script of the framework of
Blazor — was **not part of the published output of the desktop application**.

Why: the files of the framework of Blazor are not ordinary files of the project. They are static
web assets of the package `Microsoft.AspNetCore.Components.Server` and they are written into the
folder `wwwroot` **when the web application is published**. The desktop application is not a web
project, so its publish did not contain them. The desktop application copies the folder `wwwroot`
of the web project, but that folder does not contain `_framework`.

What happened then:

1. The page of the sign in was rendered by the server (SSR) and looked perfect.
2. The script of the framework was missing, so the **circuit of Blazor was never connected** and
   the page stayed a static document.
3. The `EditForm` of the page has a handler of the framework for the event `submit`. Without the
   framework the handler does not exist, therefore the browser sent the form as an **ordinary
   POST** to `/login`, without the antiforgery token.
4. `app.UseAntiforgery()` rejected that request with **400 Bad Request** — the protection worked
   exactly as it should, the page was simply not interactive any more.

The same explanation fits every symptom: the pages opened, the navigation worked (it is a normal
link), and only the forms failed.

**Why the tests did not see it:** the end to end tests start the web application with
`dotnet run` from the source folder. There the static web assets are delivered through the
manifest of the build (`staticwebassets.endpoints.json`), the file is found and the page is
interactive. The published desktop application has no such manifest, and the file was missing.

## 2. The repair

No protection was switched off. The antiforgery middleware, the authorization and the
authentication are exactly as they were.

| File | Change |
|---|---|
| `src/FinanceFlow.Desktop/FinanceFlow.Desktop.csproj` | The target `AddWebApplicationStaticFilesToPublish` publishes the web application into a temporary folder and adds its folder `wwwroot` (with `_framework`) to the output of the desktop application. The property `StaticWebAssetsEnabled` is turned on. The copy of the folder `wwwroot` of the web project is skipped when the application is published, so there are no two files with the same name. |
| `src/FinanceFlow.Web/Program.cs` | `MapStaticAssets()` is now only called when the manifest of the build exists (`File.Exists`). Before, the call was wrapped in a `try/catch` that hid the real problem. The middleware of the static files serves the folder `wwwroot` of the published web application. |
| `src/FinanceFlow.Desktop/ServerHost.cs` | The address of the server is written into the file `barsig-server.url` next to the executable and removed when the program ends. The user can open the same application in a normal browser, the tests find the server of the published program. |
| `tests/FinanceFlow.E2ETests/DesktopEndToEndTests.cs` | Two new regression tests of the **published** program. |

## 3. The evidence

The published application after the repair:

| Request | Result |
|---|---|
| `GET /login` | 200 |
| `GET /_framework/blazor.web.js` | **200** (before the repair: 404) |
| `GET /_framework/blazor.server.js` | 200 |
| `GET /FinanceFlow.Web.styles.css` | 200 |
| `GET /app.css`, `GET /img/BarsiG_Logo.png` | 200 |
| `POST /login` **without** an antiforgery token | **400 — the protection is active** |

## 4. The new regression tests

`tests/FinanceFlow.E2ETests/DesktopEndToEndTests.cs` starts the **real published `BarsiG.exe`**,
opens the address of its server in a real browser (Chromium of Playwright) and repeats the
scenarios that failed:

1. `ThePublishedApplication_SignsInAndRegistersWithoutAnHttpError`
   * the page of the sign in is interactive and contains no error,
   * a `POST` without the antiforgery token is rejected with 400 (the protection is not weakened),
   * the sign in of the demonstration customer reaches the dashboard,
   * the registration of a new customer reaches the dashboard and creates **exactly one** customer
     (the test reads the database of the application in the read only mode),
   * the program is stopped and started again, the new customer signs in again.
2. `ThePublishedApplication_ShowsEveryPageOfTheBankAndSignsOut`
   * twelve pages are opened one after the other: dashboard, accounts, transfers, deposits,
     virtual cards, security, transactions, budgets, analytics, forecast, insights, goals,
   * no page shows `HTTP ERROR 400` or `HTTP ERROR 500`,
   * the session stays alive during the navigation (no page sends the customer to the sign in),
   * the sign out returns to the page of the sign in.

The database of the application is **never deleted** by the test, it is only read.

## 5. The result of the whole suite

| Project | Kind | Result |
|---|---|---|
| `FinanceFlow.UnitTests` | unit | 127 / 127 |
| `FinanceFlow.ComponentTests` | component | 25 / 25 |
| `FinanceFlow.IntegrationTests` | integration | 15 / 15 |
| `FinanceFlow.E2ETests` | end to end | 41 / 41 |
| **Total** | | **208 / 208** |

The build has no warning and no error.

---

# Stage 10 — the final session (2026-11-14)

The work of this session in the order of the priorities of the university requirements.

## 10.1 Git: the repository is safe and private

| Item | Result |
|---|---|
| Remote | `https://github.com/Ru1nE4/BarsiG-IFP-Final-Project.git` (exists, **private**: the public API answers 404, `git ls-remote` works with the credentials) |
| History | one sanitised root commit `0902536` "Initial BarsiG project - sanitized" |
| Old tainted commits | `2f69b82` and `6701e38` are **not reachable** from `main` nor from `origin/main`; they still exist in the local object store and in the reflog until `git gc` prunes them |
| The old demo password | **absent** from the new commit, from every reachable ref and from the working tree (verified by `git grep` over every commit and by a recursive search of the working tree) |
| Tracked files | 248 source files; no database, no backup folder, no `bin/`, `obj/`, `publish/`, no `.env`, no Playwright output |
| Working tree | clean at the start of this session |

The history was cleaned **before** the first publication, so the secret never reached GitHub.

## 10.2 The security of the administrator (a real bug, fixed)

`src/FinanceFlow.Infrastructure/Seeding/AdminSeeder.cs` promoted **any** existing account to
`Admin` when its e-mail matched the configured administrator address, and it never checked the
password of that account. Anybody could register `admin@barsig.app` first and would have
received the rights of the bank.

The new policy is deliberately narrow:

| Situation | New behaviour |
|---|---|
| The address is free | The administrator is created, the customer and the profile are written by **one** transaction |
| The address belongs to an administrator | Nothing is changed. The configured password is compared with the stored one and a warning is written when they differ |
| The address belongs to a normal customer | **Refused.** The account is not promoted, the password is not changed, a warning names the address |
| The password is shorter than 12 characters | Refused, nothing is created |

The outcome is reported as `AdminSeedOutcome` and written in the log of the server.
Nine integration tests cover the policy: `tests/FinanceFlow.IntegrationTests/AdminSeederTests.cs`.

**What was not done:** the password of the administrator in the existing local database was not
changed. Changing it is an operation on the data of the user and it needs his confirmation.
The safe procedure is written in `reports/AUDIT_REPORT.md`.

## 10.3 The static web assets in production (a real bug, fixed)

`/_framework/blazor.web.js`, `/app.css` and `/FinanceFlow.Web.styles.css` answered **404** when
the web application was started outside the development environment (`dotnet run` without a
launch profile, or any production host). The manifest of the build was looked for in the content
root, where it does not exist. The interface stayed a dead page and the forms of the sign in were
sent without the token of the antiforgery.

The fix is one line in `src/FinanceFlow.Web/Program.cs`:

```csharp
builder.WebHost.UseStaticWebAssets();
```

After the fix, all three files answer **200** in production. The published `BarsiG.exe` was not
broken by this, because the desktop project copies the published `wwwroot` of the web application
into its own output and `UseStaticFiles` serves it directly.

## 10.4 The unified title bar of the desktop application

| File | What was done |
|---|---|
| `src/FinanceFlow.Desktop/MainWindow.xaml` | The system caption is replaced by the bar of BarsiG: `WindowChrome` with `CaptionHeight=0`, `ResizeBorderThickness=6`, `UseAeroCaptionButtons=False`. The bar holds the logo, the name of the application, the small button of the server and the three real buttons of the window |
| `src/FinanceFlow.Desktop/MainWindow.xaml.cs` | Dragging with `DragMove()`, double click to maximize, the glyph of the button follows the state, the right click of the maximize button opens the menu of the system (Windows 11 shows the layouts of the windows there) |
| `src/FinanceFlow.Desktop/ThemePalette.cs` | **NEW.** The single source of the colours of the window. The values are the same as the values of the stylesheet of the web application |
| `src/FinanceFlow.Desktop/NativeMethods.cs` | **NEW.** `WM_GETMINMAXINFO` keeps a maximized window inside the work area of the monitor |
| `src/FinanceFlow.Web/wwwroot/js/financeflow.js` | `applyTheme` tells the host which colour scheme is active, `getCurrentTheme` lets the window read it after the page was loaded |

**The theme has one switch, not two.** The web application owns the colour scheme, it keeps it in
the storage of the browser and it sends the change to the window through the bridge of WebView2.
The message carries nothing but the name of the colour scheme. The window reads the same value
again after every navigation, therefore the title bar and the interface can never disagree.

**The separator** between the title bar and the interface is one pixel high and uses the colour
of the border of the theme (`#DCE3DE` in the light scheme, `#2A3A40` in the dark one). It is
neither bright white nor a shadow.

**The server button** shows a small server glyph and a small dot. The long address is never shown
in the bar. The click opens a small window with the title **BarsiG Server**, the real origin
(`http://127.0.0.1:<port>`) and the button **Copy address**, which copies **only** the origin.
The button **Open in browser** opens the same address in the default browser. `Escape` closes the
window, the focus goes to the button of the copy, and the window follows the theme.

**The dot of the state is real.** The window asks the endpoint `/health` every five seconds and
the dot is green only when the server answers. `/health` was added to `Program.cs`; it says
nothing about the customers and nothing about the money.

## 10.5 The scrollbars of BarsiG

The scrollbars were the standard ones of the browser, they interrupted the interface.

* `src/FinanceFlow.Web/wwwroot/app.css` — the scrollbars are narrow (12 px), round and quiet.
  The thumb of the light scheme is `#A8BCB1` and becomes `#477C71` under the pointer, the thumb
  of the dark scheme is `#3A4F57` and becomes `#6FA79A`. The rules live in
  `@supports selector(::-webkit-scrollbar)`, because Chromium ignores the pseudo elements of the
  scrollbar as soon as `scrollbar-width` is set. Firefox keeps the standard properties.
* `src/FinanceFlow.Desktop/App.xaml` — the same look for the native controls of the window.

The scrollbar is **never hidden**. The wheel, the touchpad, the keys, the touch and the drag of
the thumb keep working, the horizontal scrolling of the tables keeps working, and the mode of a
high contrast keeps the colours of the system.

## 10.6 The functional programming of the course

| Concept | Where it lives now | Status |
|---|---|---|
| `Pipe` | `Function.Pipe`, used in `TransactionClassifier.Total` | Core |
| `Curry` and partial application | `Function.Curry`, used in `TransactionClassifier.OfKind` and `AtLeast` | Core |
| `Compose` | `Function.Compose`, used in `TransactionClassifier.Total` | Core |
| `Combine` | `ResultExtensions.Combine`, used in `GoalValidator.Validate` | Core |
| `Map` and `BindAsync` | `SavingsGoalService.ContributeAsync` | Core |
| Recursion | `BankingMath.ProjectBalance` (the projection of a deposit) | Core, projection only |

**New:** `src/FinanceFlow.Domain/Analytics/TransactionClassifier.cs` — the pure classification of
the transactions: the curried predicates, the pipe, the lazy operators of the project and the
LINQ of the standard library in one small module.

**Changed:** `AnalyticsCalculator.TotalIncome` and `TotalExpense` now use the classifier.
`SavingsGoalService.ContributeAsync` became one pipeline of two fallible steps; the silent
fallback of zero is gone. `GoalValidator` validates the note of the goal as well, and it uses
`Combine` for the two checks of the title.

**New:** `docs/IFP_CONCEPTS.md` — every concept of the course with the real file, the real
method, a real snippet, the reason, the benefit and the sentence for the defence. It also
separates the topics of the lectures from the useful patterns that are not lecture topics, and it
contains four exercises for the live coding.

## 10.7 The documentation

| File | State |
|---|---|
| `docs/IFP_CONCEPTS.md` | **NEW** |
| `docs/GIT_COLLABORATION_GUIDE.md` | **NEW** — the beginner guide for the collaboration of Azamat and Dauit |
| `docs/BarsiG_Final_Report.docx` | **NEW** — the report of the university in the Word format, built from the Markdown files by `tools/build-report-docx.ps1` |
| `docs/BarsiG-Presentation.pptx` | the slides of the previous session, they still show the old title bar and the old scrollbars |
| `docs/BARSIG_IMPLEMENTATION_PLAN.md` | this stage |
| `reports/AUDIT_REPORT.md` | the audit of this session |

## 10.8 The verified state of the tests

| Project | Kind | Before | After |
|---|---|---|---|
| `FinanceFlow.UnitTests` | unit | 127 | **149** (+22) |
| `FinanceFlow.ComponentTests` | component | 25 | 25 |
| `FinanceFlow.IntegrationTests` | integration | 15 | **24** (+9) |
| `FinanceFlow.E2ETests` | end to end | 41 | 41 |
| **Total** | | 208 | **239** |

The release build has **0 warnings and 0 errors**.

## 10.9 What was checked in the real browser

Checked in Chrome with the *browse* tool, against the server of the release build:

* the page of the sign in, the registration and the sign in of the demonstration customer,
* the dashboard with the banner and the charts,
* the switch of the theme: the class of the document becomes `theme-dark`, the storage of the
  browser keeps `dark`, therefore the choice survives a restart of the application,
* the scrollbars of the page and of the side bar,
* the layout at 430 px, at 1600x900 and the page of the deposit with the new projection.

**Fixed during the check:** the colours of the background of the pages of the sign in were the
colours of a ready made template (violet and sky blue). They are now the colours of the brand of
BarsiG. The ring of the focus around the heading that Blazor focuses after the navigation was
hidden, because a heading is not an interactive element.

## 10.10 The next steps

1. The password of the administrator in the existing local database has to be rotated by the
   owner of the project, the procedure is in `reports/AUDIT_REPORT.md`.
2. Dauit has to accept the invitation of the repository, the steps are in
   `docs/GIT_COLLABORATION_GUIDE.md`.
3. The slides and the report of the university have to be refreshed with the new screenshots.
4. The published `BarsiG.exe` has to be built again and to be checked once more by hand.
