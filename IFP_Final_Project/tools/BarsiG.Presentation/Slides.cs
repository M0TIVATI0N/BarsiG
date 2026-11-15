namespace BarsiG.Presentation;

/// <summary>One slide of the presentation of BarsiG.</summary>
/// <param name="Title">The title of the slide.</param>
/// <param name="Bullets">The lines of the slide, the first one can be a subtitle.</param>
/// <param name="Footer">The small line at the bottom of the slide.</param>
public sealed record Slide(string Title, IReadOnlyList<string> Bullets, string Footer = "");

/// <summary>The content of the presentation of BarsiG.</summary>
public static class Slides
{
    /// <summary>The colour of the night, the darkest tone of the palette.</summary>
    public const string Midnight = "101820";

    /// <summary>The colour of the slate, the second tone of the palette.</summary>
    public const string Slate = "1C2B35";

    /// <summary>The colour of the sage, the accent of the palette.</summary>
    public const string Sage = "477C71";

    /// <summary>The colour of the off white paper of the palette.</summary>
    public const string OffWhite = "F4F6F4";

    /// <summary>The colour of the mist, the pale tone of the palette.</summary>
    public const string Mist = "D5E0DA";

    /// <summary>Builds the slides of the defence of the project.</summary>
    public static IReadOnlyList<Slide> Build() =>
    [
        new Slide(
            "BarsiG",
            [
                "Digital Banking Simulator",
                "Azamat Ziadanov · Dauit Khassenov",
                "Introduction to Functional Programming (FP 3222) · teacher Tsoi Pavel",
                "All money in this project is virtual."
            ]),

        new Slide(
            "The team",
            [
                "Azamat Ziadanov — the domain and the application layer, the ledger, the security, the unit tests",
                "Dauit Khassenov — the interface, the Windows application, the charts, the end to end tests, the documentation",
                "Both students wrote the code, both students can explain every part of the project",
                "Course: FP 3222, faculty of informatics"
            ]),

        new Slide(
            "The aim and the goals",
            [
                "The aim: a working banking simulation with virtual money and a trustworthy ledger",
                "The goals: accounts, transfers, deposits, an administrator, virtual cards, receipts, a security centre",
                "The money must never appear out of nothing — every movement is a double entry transaction",
                "One server, many clients: a browser and a Windows application talk to the same database",
                "The business core stays pure: immutable records, Result<T>, pure functions, LINQ pipelines"
            ]),

        new Slide(
            "The plan of the project",
            [
                "Stage 0 — the audit of FinanceFlow, the backup, the plan",
                "Stages 1–3 — the accounts, the ledger, the transfers, the administration of the Demo Bank",
                "Stages 4–6 — the deposits, the security, the virtual cards, the receipts",
                "Stages 7–8 — the identity of BarsiG, the Windows application",
                "Stages 9–10 — the tests, the documentation, the presentation and the defence"
            ]),

        new Slide(
            "The technologies and the comparison",
            [
                "C# 13 and .NET 10 — one language for the domain, the server and the desktop application",
                "Blazor Web App (interactive server) — the interface runs on the server, no JavaScript framework is needed",
                "WPF and Microsoft Edge WebView2 — the Windows client reuses the same components",
                "Entity Framework Core 10 and SQLite — one file, one authoritative database",
                "xUnit, bUnit and Playwright — the rules are proved by 200 automated tests",
                "Why not MAUI: it would duplicate the interface and break the existing component tests"
            ]),

        new Slide(
            "The architecture",
            [
                "Domain — the pure core: no dependency, no database, no interface",
                "Application — the use cases, every one of them returns Result<T>",
                "Infrastructure — SQLite, the ledger, SignalR, the workers, the PDF",
                "Web — the Blazor server of the simulation",
                "Desktop — the Windows client of the same server",
                "The interfaces point inwards, the side effects (clock, hashing, notifications) are interfaces too"
            ]),

        new Slide(
            "The ledger — the heart of the project",
            [
                "BankAccount — the containers of the money",
                "LedgerTransfer — one business transaction",
                "LedgerEntry — one immutable debit or credit line",
                "Every committed transaction has exactly two entries with the same amount",
                "The sum of all entries is zero: the money is moved, never created",
                "The balance of an account is always the sum of its entries",
                "The rows of the ledger are never changed, a correction is a compensating transaction"
            ]),

        new Slide(
            "The atomicity and the protection against a double payment",
            [
                "The transfer, the two entries and the two balances are written in one database transaction",
                "Microsoft.Data.Sqlite serializes the writers of the database",
                "A gate inside the process protects the read, the check and the write",
                "A version on the row of the account detects a lost update",
                "A unique index on (customer, idempotency key) — the same request never moves the money twice",
                "A failed transfer leaves no entry and no balance change"
            ]),

        new Slide(
            "The accounts and the reserve of the Demo Bank",
            [
                "Every new customer receives one primary account with exactly 0 ₸",
                "The Demo Bank owns a reserve of 1 000 000 ₸, it is configured in the settings",
                "The administrator moves money out of the reserve, he never edits a balance",
                "The reserve decreases by exactly the amount the customer receives",
                "A counterpart account holds the issued money, so the total supply of the simulation is zero",
                "The oldest data of FinanceFlow stays untouched: the migrations are purely additive"
            ]),

        new Slide(
            "The transfer between two customers",
            [
                "1. Choose the recipient, the directory hides the middle of the e-mail address",
                "2. Confirm the details of the recipient",
                "3. Enter the amount and the description",
                "4. Review: sender, recipient, amount, the balance after the transfer",
                "5. Confirm with the transaction PIN or with the password — the server checks it",
                "6. The number of the transaction, the new balances and the PDF receipt",
                "The other client sees the new balance immediately, the channel is SignalR"
            ]),

        new Slide(
            "The deposits and the interest",
            [
                "A deposit is its own bank account, the money moves with a real ledger transaction",
                "The interest of a month: principal × rate / 12, rounded to whole cents",
                "The interest is paid out of the reserve, so the total supply never changes",
                "The month that was paid is stored in the database, a double accrual is impossible",
                "For the defence the clock of the simulation can be moved forward (development only)"
            ]),

        new Slide(
            "The security",
            [
                "PBKDF2 with 100 000 iterations and a random salt for every password",
                "The transaction PIN is hashed with the same function — a second secret, not a second factor",
                "The authenticator is a one time password of RFC 6238, proved with the vectors of the standard",
                "Five wrong passwords block the account for five minutes, the server has a rate limiter too",
                "The sessions are written into the database, the customer sees them and can terminate them",
                "Every sensitive action writes an immutable row into the audit",
                "The endpoints that move money need an antiforgery token"
            ]),

        new Slide(
            "The functional programming",
            [
                "Immutable records: every entity, every read model, every stage of a pipeline",
                "Result<T> instead of exceptions: a problem is a value, it can never be forgotten",
                "Pure functions: the interest, the limits, the validation, the one time password",
                "LINQ as a pipeline: the reports and the aggregation of the ledger",
                "The own lazy operators: Filter, Transform, Fold, Head",
                "Function composition: Function.Compose, Function.Flip",
                "Type state pipelines: a transaction can not exist without passing the validation",
                "Money is a total type: the amount is an integer number of cents"
            ]),

        new Slide(
            "The Windows application and the web application",
            [
                "BarsiG.exe — WPF with Microsoft Edge WebView2, the approved icon of BarsiG",
                "The server of the simulation runs inside the desktop process, on a free port",
                "The user never types an address, closing the window stops the server",
                "A remote server can be used: Desktop:ServerUrl in the settings of the client",
                "The web application does not depend on the desktop application",
                "Both are only clients of the same authoritative backend and the same database"
            ]),

        new Slide(
            "The tests — 200 automated tests",
            [
                "127 unit tests: the money, the results, the rules of the bank, the one time password",
                "25 component tests: the reusable components, the charts, the modals, the toasts",
                "15 integration tests against a real SQLite file: the double entry, the conservation of the money,",
                "the idempotency, two concurrent transfers, the isolation of the customers, the deposits",
                "33 end to end tests in a real browser, including the whole acceptance scenario",
                "The build is clean: no warning, no error"
            ]),

        new Slide(
            "The demonstration",
            [
                "Register two customers, both accounts show 0 ₸",
                "The administrator gives 100 000 ₸ out of the reserve — the reserve shows 900 000 ₸",
                "The first customer sends 25 000 ₸ — the second one sees it immediately",
                "30 000 ₸ move into a deposit — the primary account shows 45 000 ₸",
                "The receipt is downloaded as a PDF with the identity of BarsiG",
                "A virtual card is frozen and unfrozen, the security page shows the sessions",
                "After the restart every balance and every entry of the ledger is still there"
            ]),

        new Slide(
            "The relevance and the conclusion",
            [
                "The project shows how a financial system keeps its promises: no money without an entry",
                "The functional programming of the course is the tool that makes the rules provable",
                "The same server serves a browser, a Windows application and a second computer",
                "Every line of the ledger is an immutable record — the same idea as the records of the course",
                "The project is ready for the questions of the examiner"
            ])
    ];
}
