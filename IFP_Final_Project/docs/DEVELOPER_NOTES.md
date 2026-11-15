# Developer notes

This document explains how the code is written, which conventions are used and how the project can
be extended. It is meant for a developer who wants to change FinanceFlow.

---

## 1. Conventions

| Rule | Reason |
|---|---|
| The business logic lives in `FinanceFlow.Domain` and is pure | It can be tested without any infrastructure |
| The domain types are immutable `record`s | A change is always visible in the source code (`with` expression) |
| Amounts are `Money`, never `decimal` or `double` | No rounding errors in a financial application |
| Dates in the domain are `DateOnly`, moments are `DateTimeOffset` | A transaction happens on a day, not at a second |
| Every operation that can fail returns `Result<T>` | The caller can never forget the failure |
| Exceptions are reserved for programming errors | A wrong input of the user is not an exceptional situation |
| The names of the files, the classes and the variables are in English | The whole code base speaks one language |
| Every public type has a short XML comment | The documentation is generated from the code |
| The interfaces of the outer layers point inwards | `Domain` has no dependency at all, `Application` only on `Domain` |

---

## 2. Where to put a new feature

| Kind of change | Where |
|---|---|
| A new rule of the business logic | `FinanceFlow.Domain` |
| A new use case | A service in `FinanceFlow.Application/Services` |
| A new way to store or to read the data | A repository in `FinanceFlow.Infrastructure/Persistence` |
| A new part of the interface | A component in `FinanceFlow.Web/Components/Shared` |
| A new page | A component in `FinanceFlow.Web/Components/Pages` and a link in `NavMenu.razor` |
| A new table or a new column | An EF Core migration, see the command below |

---

## 3. Adding a feature, step by step

The example adds a field `Note` to the savings goal.

```bash
# 1. the domain
#    src/FinanceFlow.Domain/Entities/Entities.cs
public sealed record SavingsGoal(
    Guid Id,
    Guid UserId,
    string Title,
    Money TargetAmount,
    Money SavedAmount,
    DateOnly? TargetDate,
    GoalStatus Status,
    string? Note,          // <- the new field
    DateTimeOffset CreatedAtUtc);
```

```bash
# 2. the configuration of the table
#    src/FinanceFlow.Infrastructure/Persistence/Configurations/EntityConfigurations.cs
builder.Property(goal => goal.Note).HasMaxLength(280);
```

```bash
# 3. the migration
dotnet tool restore
dotnet ef migrations add AddGoalNote -p src/FinanceFlow.Infrastructure -s src/FinanceFlow.Infrastructure
```

```bash
# 4. the read model and the projection
#    src/FinanceFlow.Application/Dtos/Dtos.cs
#    src/FinanceFlow.Application/Mapping/EntityMapper.cs
```

```bash
# 5. the service
#    src/FinanceFlow.Application/Services/SavingsGoalService.cs
```

```bash
# 6. the interface
#    src/FinanceFlow.Web/Components/Pages/Goals.razor
```

```bash
# 7. the tests
dotnet test
```

The order of the steps is important: the domain first, then the storage, then the use case, then
the interface. The compiler points to the place that has to be changed next.

---

## 4. How the result type is used

```csharp
// a pure validator
public static Result<ValidatedTransaction> Validate(TransactionDraft draft);

// a service
public async Task<Result<TransactionItem>> CreateAsync(Guid userId, TransactionDraft draft)
{
    Result<ValidatedTransaction> validated = await TransactionValidator.Validate(draft)
        .BindAsync(value => EnsureCategoryAsync(userId, value, cancellationToken));

    return await validated
        .BindAsync(value => StoreAsync(userId, value, cancellationToken));
}

// the interface
Result<TransactionItem> result = await TransactionService.CreateAsync(userId, draft);

if (result is not Ok<TransactionItem> ok)
{
    _error = result.Error;
    return;
}

ToastService.Success("Transaction added", ok.Value.CategoryName);
```

Rules:

* `Map` changes the value, but it cannot fail.
* `Bind` continues with another step that can fail.
* `BindAsync` is the asynchronous version of `Bind`.
* `Match` is the only place where the two cases are inspected.
* `Tap` runs a side effect (a log, a notification) without changing the result.

---

## 5. The lazy operators

`FinanceFlow.Domain/Common/LazyOperators.cs` contains operators that are written by hand with
`yield return`. They are the direct continuation of the course assignment "Extension Methods and
LINQ".

```csharp
public static IEnumerable<T> Filter<T>(this IEnumerable<T> source, Func<T, bool> predicate)
{
    foreach (T item in source)
    {
        if (predicate(item))
        {
            yield return item;
        }
    }
}
```

They are used where the filter is applied to the transactions:

```csharp
IEnumerable<Transaction> query = all;

if (filter.Kind is { } kind)
{
    query = query.Filter(transaction => transaction.Kind == kind);
}
```

The standard LINQ operators are used everywhere else. The hand written ones show that the
deferred execution is understood and not just used.

---

## 6. The state of the client

Three state containers, all of them of the type `StateContainer<TState>`:

```csharp
public sealed class SessionState : StateContainer<SessionInfo>
{
    public void SignIn(UserSession session) => State = new SessionInfo { ... };
}
```

A new state container is created in `Program.cs` and published with the reusable component
`StateCascadingValue`:

```razor
<StateCascadingValue TState="IReadOnlyList<ToastMessage>" TValue="IReadOnlyList<ToastMessage>" Container="ToastService">
    <ToastHost />
</StateCascadingValue>
```

**Important:** a Blazor component is only re-rendered when one of its parameters changes. A
component that reads its data only from a service is never re-rendered. Therefore the data has to
arrive as a parameter (a cascading parameter is a parameter as well).

---

## 7. The charts

The charts are inline SVG. The geometry is calculated by the pure class `ChartGeometry`:

```csharp
IReadOnlyList<(double X, double Y)> points = ChartGeometry.Project(values, width, height, 8, 8, 12, 12, min, max);
string path = ChartGeometry.BuildLinePath(points);
```

**Important:** every number that goes into the SVG markup is written with
`ChartGeometry.Number(value)`. The SVG format only understands a point as the decimal separator,
the comma of the current culture would break the picture.

---

## 8. The style

The whole design is in one file, `wwwroot/app.css`.

* The colours are CSS variables. The dark theme is the class `theme-dark` on the root element.
* The layout is a CSS grid. The cards use `repeat(auto-fit, minmax(...))`, so they adapt to every
  width without a single media query.
* The media queries at the end of the file handle the sidebar of a phone.

A new component should reuse the existing classes (`card`, `btn`, `field`, `table`, `badge`), so
the interface stays consistent.

---

## 9. The tests

```bash
dotnet test                                             # everything
dotnet test tests/FinanceFlow.UnitTests                # the business logic
dotnet test tests/FinanceFlow.ComponentTests           # the components
dotnet test tests/FinanceFlow.E2ETests                 # the browser
dotnet test --collect:"XPlat Code Coverage"           # with the coverage
```

The end to end tests start the real application:

```csharp
ProcessStartInfo startInfo = new("dotnet") { WorkingDirectory = webProject };
startInfo.ArgumentList.Add("run");
startInfo.ArgumentList.Add("--no-build");
startInfo.ArgumentList.Add("--");
startInfo.ArgumentList.Add("--urls");
startInfo.ArgumentList.Add(BaseUrl);
```

The tests use their own database file in the temporary folder, so they never touch the data of a
working installation. The browser has to be installed once:

```bash
cd tests/FinanceFlow.E2ETests/bin/Debug/net10.0
powershell -ExecutionPolicy Bypass -File playwright.ps1 install chromium
```

The end to end tests must wait until the page is interactive, otherwise Blazor replaces the parts
of the page that were rendered on the server and the click misses its target:

```csharp
await page.WaitForFunctionAsync("() => window.__financeFlowReady === true");
```

---

## 10. Things that are easy to get wrong

| Mistake | Consequence | The correct way |
|---|---|---|
| Two antiforgery tokens in one form | The sign in fails with *"A valid antiforgery token was not provided"* | Only the token of the `EditForm` |
| `HttpClient` for the sign in from the server | The browser never receives the cookie | The request has to be made by the browser (`financeFlow.signIn`) |
| `DateTime.Now` on the client and `DateTime.UtcNow` on the server | The date of today differs by one day and the validation fails | Use `DateTime.UtcNow` everywhere |
| A number of the current culture in the SVG | The chart is not drawn | `ChartGeometry.Number(value)` |
| A component that reads only from a service | The interface does not update | Pass the data as a parameter |
| A global render mode with `[ExcludeFromInteractiveRouting]` | The page is interactive anyway and the form does not work | Use the JSON endpoints of the application layer |

---

## 11. The useful commands

```bash
dotnet build                                            # build everything
dotnet build -c Release                                 # the release build
dotnet test                                             # all the tests
dotnet test --filter FullyQualifiedName~MoneyTests     # one test class
dotnet run --project src/FinanceFlow.Web               # start the application
dotnet ef migrations add Name                           # a new migration
dotnet ef database update -p src/FinanceFlow.Infrastructure   # apply the migrations
dotnet ef migrations remove                             # remove the last migration
```

---

## 12. The dependencies

Every version is declared in `Directory.Packages.props`. A new package is added there once and
then referenced by the projects without a version.

| Package | Why |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | The database |
| `Microsoft.EntityFrameworkCore.Design` | The migrations |
| `Microsoft.Playwright` | The end to end tests |
| `bunit` | The component tests |
| `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` | The test framework |
| `coverlet.collector` | The coverage report |
