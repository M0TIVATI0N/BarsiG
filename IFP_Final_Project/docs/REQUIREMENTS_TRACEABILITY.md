# Traceability of the grading criteria

The final project requirements contain 20 criteria, each of them worth 5 %. This document shows
where every criterion is fulfilled and how it can be demonstrated during the defence.

The status was checked by running the application and the tests, see
[`reports/AUDIT_REPORT.md`](../reports/AUDIT_REPORT.md).

| Status | Meaning | Count |
|---|---|---|
| **confirmed** | implemented and verified by a test or by a manual check | 17 |
| **partly** | implemented, but a part is missing or was not verified on every platform | 3 |
| **missing** | not implemented | 0 |

> The project was upgraded from **FinanceFlow** to **BarsiG — Digital Banking Simulator**.
> The 20 criteria of the course are listed first, the new requirements of BarsiG are listed
> in the second table. The measured numbers are in `reports/AUDIT_REPORT.md`.
> The earlier implementation snapshot contained 208 tests. Final local Release verification now passes **265 tests**: 177 unit, 25 component, 31 integration, 29 Web E2E and 3 Desktop E2E.

| # | Criterion (5 % each) | Status | Where it is implemented | How it is shown |
|---|---|---|---|---|
| 1 | **Code Structure and Design** | confirmed | Four layers: `Domain` (no dependencies), `Application`, `Infrastructure`, `Web`. The interfaces point inwards, every service has one reason to change | The diagram in `docs/ARCHITECTURE.md`, the folder tree in the README |
| 2 | **Component Reusability** | confirmed | 14 reusable components in `Components/Shared` plus `StateCascadingValue` in `Components/State`: `StatCard`, `PageHeader`, `ProgressBar`, `MoneyText`, `CategoryBadge`, `EmptyState`, `ConfirmDialog`, `ToastHost`, `MonthPicker`, `LineChart`, `BarChart`, `DonutChart`, `AppIcon`, `TransactionEditor`. Parameters, cascading values and event callbacks are used everywhere | The component library, the `ClickAndWaitAsync` helper of the tests |
| 3 | **Cross-Platform Compatibility** | partly | ASP.NET Core, no Windows specific code, a single stylesheet without any platform specific hack. The application is a self contained server | The application is started on Windows; the same build runs on macOS and Linux with the .NET runtime, but it was **not tested there** |
| 4 | **State Management** | confirmed | `StateContainer<T>` as the base of `SessionState`, `ThemeState` and `ToastService`; the reusable component `StateCascadingValue` publishes them; the pages subscribe to the real time events | The state folder, the toast that appears after every action |
| 5 | **UI Design** | confirmed | A design system in one stylesheet: tokens, cards, buttons, tables, forms, toasts, modals, a light and a dark theme | The 11 screenshots in `docs/images`, the live demonstration |
| 6 | **UX** | confirmed | The navigation is always visible, the current page is highlighted, the actions are in the header, the forms show the problem next to the field, every list has an empty state, the destructive actions ask for a confirmation | The demonstration of adding and deleting a transaction |
| 7 | **Responsiveness** | confirmed | CSS grid with `auto-fit`, one breakpoint at 1024 px for the sidebar, one at 640 px for the typography and the cards | `docs/images/dashboard-mobile.png` at 430 px |
| 8 | **Core Functionality** | confirmed | Dashboard, transactions, categories, budgets, analytics, forecast, insights, goals, export, authentication — all of them work and are covered by the tests | The live demonstration and the 150 automated tests |
| 9 | **Advanced Features** | partly | SignalR hub for the real time updates (verified: the balance of a second page changes automatically), a background worker for the recurring transactions, a forecast with a confidence interval, a rule based insight engine, PBKDF2 hashing, a service worker (PWA), an export in two formats | The hub in `Infrastructure/RealTime`, the worker in `Infrastructure/Background`. **Gap:** the recurring transactions have no interface yet, see the task T3 |
| 10 | **Error Handling and Resilience** | confirmed | `Result<T>` instead of exceptions for every expected problem, a validator for every field, a message next to the field, a toast for the result, the reconnect window of Blazor, a global exception page | The demonstration of a wrong amount, the code of the `Result` type |
| 11 | **Efficiency** | confirmed | The dashboard loads the data once and calculates everything from it, `AsNoTracking` for the reading queries, indexes on the user, the category and the date, the charts are SVG without a library, the amount is an integer | The `DashboardService` that loads the data once, the indexes in the migrations |
| 12 | **Scalability** | partly | The layers can be split, the repositories hide the storage, the services depend only on the interfaces, the user data is separated by the identifier, a background worker does the heavy work outside the request | The architecture, the abstraction of the repositories. **Gap:** no load test was made, see the task T4 |
| 13 | **Resource Utilization** | confirmed | The amount is stored as cents (an integer, no rounding), the moments as Unix milliseconds, the charts are computed on the server and sent as markup, the SignalR connection is only opened when a page needs it | The value converters, the SVG charts |
| 14 | **Testing Coverage** | partly | 150 tests: 94 unit, 25 component, 31 end to end. The business core is covered by 84.5 % of the lines | `dotnet test`, the measured numbers in `reports/AUDIT_REPORT.md`. **Gap:** the infrastructure layer is covered by 1.1 %, see the tasks T1 and T2 |
| 15 | **Quality Assurance** | partly | The manual checklist in `docs/TESTING.md`: three screen sizes, the dark theme, the keyboard, two accounts at the same time, the wrong input. The automated tests run in Chromium | The checklist. **Gap:** Firefox and Edge were not tested, see the task T5 |
| 16 | **Documentation** | partly | README, architecture, user guide, developer notes, testing, this traceability matrix, the defence guide, the presentation and the audit report | The `docs` folder and the `reports` folder. **Gap:** there is no report file in DOCX or PDF, see the task T6 |
| 17 | **Innovation** | confirmed | The insight engine gives personal advice, the forecast shows a confidence interval, the charts are drawn with own SVG geometry, the type state pipeline makes the wrong order of the steps impossible, the hand written lazy operators | The insight engine, the forecast, the chart geometry |
| 18 | **Creative Use of Blazor/C#/.NET** | confirmed | Templated components with `RenderFragment`, cascading values, a reusable state container, a SignalR hub, a background service, records with pattern matching, `init` only properties, the `with` expression, a value converter of EF Core | The source code, the chart components |
| 19 | **Professionalism** | confirmed | A clean build without warnings, a central package management, a consistent naming, XML comments on the public types, no dead code, `.gitignore`, the migrations, a seed of the demonstration data | `dotnet build` without a single warning, the structure of the repository. **Gap:** there is no git history yet, see the task T7 |
| 20 | **Presentation** | partly | The presentation follows the structure that the requirements demand: the team, the topic, the aim, the goals, the plan, the comparison of the technologies, the architecture, the relevance, the methodology and the demonstration | `docs/PRESENTATION.md` is the outline of 12 slides. **Gap:** there is no PPTX file yet, see the task T8 |

---

## The requirements of the presentation

| Required part | Where it is prepared |
|---|---|
| Team members with the full names | `docs/PRESENTATION.md`, slide 2 — Azamat Ziadanov, Dauit Khassenov |
| Topic | slide 2 |
| Aim and goals | slide 3 |
| Plan of the project | slide 4 |
| Analysis of the existing technologies and a comparison | slide 5, the table of the comparison |
| Architecture (MVVM/MVC/BLoC, UML diagrams) | slides 6 and 7, the layer diagram and the class diagram |
| Relevance and practical significance | slide 8 |
| Methodology and mockups | slide 9, the screenshots of the pages |
| Demonstration of the working application | slides 10 to 12 |
| Questions and answers | `docs/DEFENSE_GUIDE.md` |

---

## The connection to the course

The project continues the topics of the course directly:

| Topic of the course | Where it is used in FinanceFlow |
|---|---|
| Assignment 6 — temporal coupling | The type state pipeline `TransactionDraft` → `ValidatedTransaction` → `Transaction` and the same for the goals |
| Assignment 6 — a side effect as a delegate | `IPasswordHasher`, `IClock` and `IDataChangeNotifier` are dependencies of the services |
| Assignment 7 — `Result<T>` without exceptions | `Result<T>`, `Ok<T>`, `Fail<T>`, `Map`, `Bind`, `BindAsync`, `Match` |
| Assignment 8 — extension methods and LINQ | The hand written lazy operators `Filter`, `Transform`, `Fold`, `Head` in `LazyOperators.cs` |
| Assignment 8 — LINQ reports | `AnalyticsCalculator` with `GroupBy`, `Sum`, `Average` |
| Pattern matching | `switch` expressions over the results, the kinds and the states |
| Function composition | `Function.Compose`, `Function.Flip` and the composed service pipelines |

---

# The traceability of the new requirements of BarsiG

> The second table follows the structure of the first one: every requirement of the upgrade
> of FinanceFlow to BarsiG, where it is implemented and how it can be shown during the defence.
> The evidence is the code, the tests and the measured result of the audit.

| # | Requirement | Status | Where it is implemented | How it is shown |
|---|---|---|---|---|
| B1 | **One authoritative backend and one database for every client** | confirmed | `src/FinanceFlow.Web/Program.cs` is the server, `src/FinanceFlow.Desktop/ServerHost.cs` starts the *same* server inside the desktop process, `FinanceFlowDbContext` is the only database | the desktop application and the browser show the same balances; the E2E test starts the real server |
| B2 | **A Windows desktop application** | confirmed | `src/FinanceFlow.Desktop`, WPF with Microsoft Edge WebView2, `BarsiG.exe` with the approved icon | the published `BarsiG.exe` was started: it listens on a free port and answers with `HTTP 200` |
| B3 | **The server starts automatically, no address to type** | confirmed | `ServerHost.FindFreePort`, `Program.CreateApp`, `Program.PrepareAsync` | the window of the desktop application opens the interface without any input |
| B4 | **A configurable remote server for a second computer** | confirmed | `Desktop:ServerUrl` in `appsettings.json`, the environment variable `BARSIG_SERVER_URL` | the setting is read by `ServerHost.StartAsync`; **not verified with two physical computers**, see the note below |
| B5 | **Accounts with virtual KZT, every new customer starts with 0 ₸** | confirmed | `AuthService.RegisterAsync` → `AccountService.CreatePrimaryAccountAsync`, `BankingSeeder` | the E2E test registers two customers and finds `0,00 ₸` on both accounts |
| B6 | **A double entry ledger with immutable audit records** | confirmed | `Domain/Banking/BankingEntities.cs`, `Infrastructure/Banking/LedgerExecutor.cs` | 15 integration tests against a real SQLite file |
| B7 | **The balance comes from a trustworthy state, not from a form field** | confirmed | the balance is written by the executor in the same transaction as the entries, `AdminService.GetOverviewAsync` compares both | `LedgerIntegrationTests.TheStoredBalanceAlwaysAgreesWithTheLedger` |
| B8 | **The money never appears or disappears** | confirmed | every movement creates two entries, the reserve has a counterpart account | `ExecuteAsync_KeepsTheTotalSupplyOfTheSimulationConstant` |
| B9 | **Atomic transfers, protection against a repeated request** | confirmed | one transaction, `LedgerWriteGate`, the unique index on `(customer, idempotency key)` | `ExecuteAsync_ExecutesTheSameIdempotencyKeyOnlyOnce`, `ExecuteAsync_LetsOnlyOneOfTwoConcurrentTransfersSucceed` |
| B10 | **Transfers between customers with the whole flow** | confirmed | `Application/Banking/TransferService.cs`, the page `Components/Pages/Transfers.razor` | the E2E test sends 25 000 ₸ from one customer to another |
| B11 | **The administrator of the Demo Bank and the reserve** | confirmed | `Application/Banking/AdminService.cs`, `Infrastructure/Seeding/AdminSeeder.cs`, the page `Components/Pages/Admin.razor` | the E2E test gives 100 000 ₸ out of the reserve and finds 900 000 ₸ |
| B12 | **No unrestricted balance editing** | confirmed | there is no such operation in the whole solution, only movements of the ledger | the search of the source finds no setter of the balance outside the executor |
| B13 | **Deposits with an educational interest** | confirmed | `Application/Banking/DepositService.cs`, `Domain/Banking/BankingMath.cs` | `DepositIntegrationTests`: 30 000 ₸ at 12 % become 30 300 ₸ |
| B14 | **No double accrual of the interest** | confirmed | `LastAccruedMonth`, the unique index on `(deposit, month)`, the deterministic idempotency key | `AccrueInterestAsync_NeverPaysTheSameMonthTwice` |
| B15 | **The simulation of the time for the defence** | confirmed | `Infrastructure/Time/AdjustableClock.cs`, the endpoint `POST /admin/time/advance` (only in the development environment) | the button of the administration page |
| B16 | **The security: authorization, ownership, hashing, throttling, step up, audit, CSRF** | confirmed | `Application/Banking/SecurityService.cs`, `Infrastructure/Banking/LoginThrottle.cs`, the rate limiter, the antiforgery metadata | the security page, the tests of the ledger and the E2E test of the isolation |
| B17 | **The authenticator based on TOTP** | confirmed | `Domain/Common/Totp.cs` (RFC 6238) | `TotpTests` with the **six test vectors of the standard** |
| B18 | **The transaction PIN, never in plain text** | confirmed | `SecurityService.SetTransactionPinAsync` uses the PBKDF2 hasher of the passwords | the security page of the interface |
| B19 | **Virtual cards** | confirmed | `Domain/Banking/VirtualCard.cs`, `Application/Banking/VirtualCardService.cs`, the page `Components/Pages/Cards.razor` | the freeze and the unfreeze in the interface, only the last four digits are stored |
| B20 | **The PDF receipt of a transfer** | confirmed | `Infrastructure/Export/ReceiptPdfRenderer.cs` (QuestPDF), the logo is embedded into the assembly | the link of the receipt on the page of the result of a transfer |
| B21 | **The login history and the sessions** | confirmed | `Domain/Banking/SecurityEntities.cs`, the page `Components/Pages/Security.razor` | the sessions and the history are real rows of the database, a session can be terminated |
| B22 | **The identity of BarsiG** | confirmed | the four approved files, `wwwroot/img`, the palette of the design system, the logo and the banner | the application and the 17 slides of the presentation |
| B23 | **The tests of the banking behaviour** | confirmed | `tests/FinanceFlow.IntegrationTests`, `tests/FinanceFlow.E2ETests/BankingEndToEndTests.cs` | 265 automated tests, all green |
| B24 | **The presentation and the report** | confirmed | `docs/BarsiG-Presentation.pptx` (17 slides, checked with the validator of the Open XML SDK), `docs/PRESENTATION.md` | the file opens in PowerPoint |
| B25 | **Verified phone-to-phone transfers** | confirmed | `TransferService`, `LedgerExecutor`, verified E.164 recipient lookup and masked review | Web E2E A→B acceptance; read-only final partner-preview ledger reconciliation |

## The features that are explicitly not implemented

| Feature | Why |
|---|---|
| QR transfers | excluded by the requirements |
| The payment of the bills of the utilities | excluded by the requirements |
| The connection to a real bank | excluded by the requirements, BarsiG is a simulation |
| The cryptocurrency | excluded by the requirements |
| The loans and the credits | excluded by the requirements |

## The note about the second computer

The configuration for a second computer is implemented and documented
(`Desktop:ServerUrl`, the HTTPS redirection outside the development environment,
the cookies with `Secure`). **It was not verified with two physical computers**,
because only one computer was available during the development. The scenario was verified
with **two clients on one computer**: the Windows application and the browser talked to the
same server and showed the same balances.
