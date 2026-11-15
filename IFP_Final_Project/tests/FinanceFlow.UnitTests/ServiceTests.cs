using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Dtos;
using FinanceFlow.Application.Export;
using FinanceFlow.Application.Services;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Domain.Models;
using FinanceFlow.UnitTests.Fakes;

namespace FinanceFlow.UnitTests;

public sealed class ServiceTests
{
    [Fact]
    public void MaskEmail_MasksPhoneIdentifiersInFailedSignInHistory()
    {
        Assert.Equal("+*******4567", FinanceFlow.Application.Banking.SecurityService.MaskEmail("+14155554567"));
    }

    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset Now = new(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Builds the account service that the authentication service needs.</summary>
    private static FinanceFlow.Application.Banking.AccountService CreateAccountService(IUnitOfWork unitOfWork) =>
        new(new FakeBankAccountRepository(), new FakeLedgerRepository(), unitOfWork, new FixedClock(Now));

    [Fact]
    public async Task RegisterAsync_CreatesTheUser()
    {
        FakeUserRepository users = new();
        FakeUnitOfWork unitOfWork = new();
        AuthService service = new(users, new FakePasswordHasher(), unitOfWork, new FixedClock(Now), CreateAccountService(unitOfWork), new FakeSecurityProfileRepository());

        Result<UserSession> result = await service.RegisterAsync(
            new RegisterRequest("  User@Example.com ", "Test User", "secret123", "secret123", "777 123 45 67"));

        Assert.True(result.IsSuccess());
        Assert.Equal("user@example.com", result.ValueOr(default!).Email);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.True(await users.ExistsAsync("user@example.com"));
    }

    [Fact]
    public async Task RegisterAsync_RejectsAnAlreadyUsedEmail()
    {
        FakeUserRepository users = new();
        AuthService service = new(users, new FakePasswordHasher(), new FakeUnitOfWork(), new FixedClock(Now), CreateAccountService(new FakeUnitOfWork()), new FakeSecurityProfileRepository());

        await service.RegisterAsync(new RegisterRequest("user@example.com", "Test", "secret123", "secret123", "+7 701 111 11 11"));
        Result<UserSession> second = await service.RegisterAsync(
            new RegisterRequest("user@example.com", "Test", "secret123", "secret123", "+7 701 111 11 12"));

        Assert.True(second.IsFailure());
        Assert.Equal("duplicate_email", ((Fail<UserSession>)second).Code);
    }

    [Fact]
    public async Task RegisterAsync_CanCreatePhoneIdentityAndNormalizesIt()
    {
        FakeUserRepository users = new();
        FakeUnitOfWork unitOfWork = new();
        AuthService service = new(users, new FakePasswordHasher(), unitOfWork, new FixedClock(Now), CreateAccountService(unitOfWork), new FakeSecurityProfileRepository());

        Result<UserSession> result = await service.RegisterAsync(
            new RegisterRequest("", "Phone Customer", "secret123", "secret123", "777 123 45 67"));

        Assert.True(result.IsSuccess());
        User user = Assert.Single(await users.GetAllAsync());
        Assert.Equal("+77771234567", user.PhoneNumber);
        Assert.False(user.IsPhoneVerified);
        Assert.EndsWith("@phone.invalid", user.Email);
    }

    [Fact]
    public async Task RegisterAsync_CreatesMultiplePhoneOnlyIdentitiesWithoutEmailCollision()
    {
        FakeUserRepository users = new();
        FakeUnitOfWork unitOfWork = new();
        AuthService service = new(users, new FakePasswordHasher(), unitOfWork, new FixedClock(Now), CreateAccountService(unitOfWork), new FakeSecurityProfileRepository());

        Assert.True((await service.RegisterAsync(new RegisterRequest("", "First", "secret123", "secret123", "+7 701 111 11 21"))).IsSuccess());
        Assert.True((await service.RegisterAsync(new RegisterRequest("", "Second", "secret123", "secret123", "+7 701 111 11 22"))).IsSuccess());

        IReadOnlyList<User> created = await users.GetAllAsync();
        Assert.Equal(2, created.Count);
        Assert.NotEqual(created[0].Email, created[1].Email);
        Assert.All(created, user => Assert.EndsWith("@phone.invalid", user.Email));
        Assert.Null(await service.TryGetPendingPhoneRegistrationAsync("+7 701 111 11 21", "wrong-password"));
        Assert.Equal(created[0].Id, await service.TryGetPendingPhoneRegistrationAsync("+7 701 111 11 21", "secret123"));
    }

    [Fact]
    public async Task LoginAsync_RejectsTheReservedPhoneOnlyEmailAlias()
    {
        FakeUserRepository users = new();
        FakeUnitOfWork unitOfWork = new();
        AuthService service = new(
            users,
            new FakePasswordHasher(),
            unitOfWork,
            new FixedClock(Now),
            CreateAccountService(unitOfWork),
            new FakeSecurityProfileRepository());

        Assert.True((await service.RegisterAsync(
            new RegisterRequest("", "Phone Customer", "secret123", "secret123", "+7 701 111 11 31"))).IsSuccess());
        User user = Assert.Single(await users.GetAllAsync());

        Result<UserSession> result = await service.LoginAsync(new LoginRequest(user.Email, "secret123"));

        Assert.True(result.IsFailure());
        Assert.Equal("reserved_phone_identity_alias", ((Fail<UserSession>)result).Code);
    }

    [Fact]
    public async Task RegisterAsync_RejectsDuplicateNormalizedPhoneIdentity()
    {
        FakeUserRepository users = new();
        FakeUnitOfWork firstUnit = new();
        AuthService service = new(users, new FakePasswordHasher(), firstUnit, new FixedClock(Now), CreateAccountService(firstUnit), new FakeSecurityProfileRepository());

        await service.RegisterAsync(new RegisterRequest("", "First", "secret123", "secret123", "777 123 45 67"));
        Result<UserSession> duplicate = await service.RegisterAsync(new RegisterRequest("", "Second", "secret123", "secret123", "+77771234567"));

        Assert.True(duplicate.IsFailure());
        Assert.Equal("duplicate_phone", ((Fail<UserSession>)duplicate).Code);
    }

    [Fact]
    public async Task LoginAsync_RejectsUnverifiedPhoneIdentity()
    {
        FakeUserRepository users = new();
        FakeUnitOfWork unitOfWork = new();
        AuthService service = new(users, new FakePasswordHasher(), unitOfWork, new FixedClock(Now), CreateAccountService(unitOfWork), new FakeSecurityProfileRepository());

        await service.RegisterAsync(new RegisterRequest("", "Phone Customer", "secret123", "secret123", "777 123 45 67"));
        Result<UserSession> result = await service.LoginAsync(new LoginRequest("777 123 45 67", "secret123"));

        Assert.True(result.IsFailure());
        Assert.Equal("phone_not_verified", ((Fail<UserSession>)result).Code);
    }

    [Fact]
    public async Task LoginAsync_AllowsVerifiedLocalDemoPhoneOnlyWhenDevelopmentFlagIsPassed()
    {
        FakeUserRepository users = new();
        (string hash, string salt) = new FakePasswordHasher().Hash("secret123");
        await users.AddAsync(new User(
            Guid.NewGuid(),
            "",
            "Phone Customer",
            hash,
            salt,
            Now,
            "+77771234567",
            IsPhoneVerified: true,
            PhoneVerificationMethod: FinanceFlow.Domain.Enums.PhoneVerificationMethod.LocalDemo));

        AuthService service = new(users, new FakePasswordHasher(), new FakeUnitOfWork(), new FixedClock(Now), CreateAccountService(new FakeUnitOfWork()), new FakeSecurityProfileRepository());

        Assert.True((await service.LoginAsync(new LoginRequest("+77771234567", "secret123"))).IsFailure());
        Assert.True((await service.AuthenticateAsync(new LoginRequest("+77771234567", "secret123"), allowLocalDemoPhone: true)).IsSuccess());
    }

    [Fact]
    public async Task RegisterAsync_RejectsDifferentPasswords()
    {
        AuthService service = new(
            new FakeUserRepository(),
            new FakePasswordHasher(),
            new FakeUnitOfWork(),
            new FixedClock(Now),
            CreateAccountService(new FakeUnitOfWork()), new FakeSecurityProfileRepository());

        Result<UserSession> result = await service.RegisterAsync(
            new RegisterRequest("user@example.com", "Test", "secret123", "other123"));

        Assert.True(result.IsFailure());
        Assert.Contains(
            ((Fail<UserSession>)result).Details,
            detail => detail.Message.Contains("do not match"));
    }

    [Fact]
    public async Task RegisterAsync_RequiresAKazakhstanMobileNumberEvenWhenEmailIsProvided()
    {
        FakeUnitOfWork unitOfWork = new();
        AuthService service = new(
            new FakeUserRepository(),
            new FakePasswordHasher(),
            unitOfWork,
            new FixedClock(Now),
            CreateAccountService(unitOfWork),
            new FakeSecurityProfileRepository());

        Result<UserSession> missing = await service.RegisterAsync(
            new RegisterRequest("user@example.com", "Test", "secret123", "secret123"));
        Result<UserSession> foreign = await service.RegisterAsync(
            new RegisterRequest("other@example.com", "Test", "secret123", "secret123", "+14155552671"));

        Assert.True(missing.IsFailure());
        Assert.True(foreign.IsFailure());
        Assert.Contains(((Fail<UserSession>)missing).Details, detail => detail.Field == "PhoneNumber");
        Assert.Contains(((Fail<UserSession>)foreign).Details, detail => detail.Field == "PhoneNumber");
    }

    [Fact]
    public async Task LoginAsync_EmailCannotBypassUnverifiedPhoneRegistration()
    {
        FakeUserRepository users = new();
        FakeUnitOfWork unitOfWork = new();
        AuthService service = new(users, new FakePasswordHasher(), unitOfWork, new FixedClock(Now), CreateAccountService(unitOfWork), new FakeSecurityProfileRepository());

        await service.RegisterAsync(
            new RegisterRequest("pending@example.com", "Pending", "secret123", "secret123", "777 123 45 67"));

        Result<UserSession> result = await service.LoginAsync(new LoginRequest("pending@example.com", "secret123"));

        Assert.True(result.IsFailure());
        Assert.Equal("phone_not_verified", ((Fail<UserSession>)result).Code);
    }

    [Fact]
    public async Task LoginAsync_ReturnsTheSessionForTheRightPassword()
    {
        FakeUserRepository users = new();
        FakePasswordHasher hasher = new();
        (string hash, string salt) = hasher.Hash("secret123");
        await users.AddAsync(new User(
            Guid.NewGuid(),
            "user@example.com",
            "Test",
            hash,
            salt,
            Now,
            "+77771234567",
            IsPhoneVerified: true,
            PhoneVerificationMethod: FinanceFlow.Domain.Enums.PhoneVerificationMethod.LocalDemo));
        AuthService service = new(users, hasher, new FakeUnitOfWork(), new FixedClock(Now), CreateAccountService(new FakeUnitOfWork()), new FakeSecurityProfileRepository());

        Result<UserSession> result = await service.LoginAsync(new LoginRequest("user@example.com", "secret123"));

        Assert.True(result.IsSuccess());
        Assert.Equal("Test", result.ValueOr(default!).DisplayName);
    }

    [Fact]
    public async Task LoginAsync_RejectsAWrongPassword()
    {
        FakeUserRepository users = new();
        AuthService service = new(users, new FakePasswordHasher(), new FakeUnitOfWork(), new FixedClock(Now), CreateAccountService(new FakeUnitOfWork()), new FakeSecurityProfileRepository());

        await service.RegisterAsync(new RegisterRequest("user@example.com", "Test", "secret123", "secret123", "777 123 45 69"));

        Result<UserSession> result = await service.LoginAsync(new LoginRequest("user@example.com", "wrong"));

        Assert.True(result.IsFailure());
        Assert.Equal("password_mismatch", ((Fail<UserSession>)result).Code);
    }

    [Fact]
    public async Task LoginAsync_RejectsAnUnknownUser()
    {
        AuthService service = new(
            new FakeUserRepository(),
            new FakePasswordHasher(),
            new FakeUnitOfWork(),
            new FixedClock(Now),
            CreateAccountService(new FakeUnitOfWork()), new FakeSecurityProfileRepository());

        Result<UserSession> result = await service.LoginAsync(new LoginRequest("nobody@example.com", "secret123"));

        Assert.True(result.IsFailure());
        Assert.Equal("email_lookup_miss", ((Fail<UserSession>)result).Code);
    }

    [Fact]
    public async Task CreateAsync_StoresAValidatedTransaction()
    {
        FakeTransactionRepository transactions = new();
        FakeCategoryRepository categories = new();
        RecordingNotifier notifier = new();
        FakeUnitOfWork unitOfWork = new();

        Guid categoryId = Guid.NewGuid();
        await categories.AddAsync(new Category(categoryId, UserId, "Food", TransactionKind.Expense, "#FF0000", "cart", false, Now));

        TransactionService service = new(transactions, categories, unitOfWork, new FixedClock(Now), notifier);

        var draft = new TransactionDraft(
            UserId,
            categoryId,
            120.50m,
            TransactionKind.Expense,
            new DateOnly(2026, 3, 10),
            "Lunch");

        Result<TransactionItem> result = await service.CreateAsync(UserId, draft);

        Assert.True(result.IsSuccess());
        Assert.Equal("Food", result.ValueOr(default!).CategoryName);
        Assert.Equal(12_050, result.ValueOr(default!).Amount.Cents);
        Assert.Single(await transactions.GetAllAsync(UserId));
        Assert.Single(notifier.Notifications);
    }

    [Fact]
    public async Task CreateAsync_RejectsANegativeAmountWithoutTouchingTheDatabase()
    {
        FakeTransactionRepository transactions = new();
        FakeCategoryRepository categories = new();

        Guid categoryId = Guid.NewGuid();
        await categories.AddAsync(new Category(categoryId, UserId, "Food", TransactionKind.Expense, "#FF0000", "cart", false, Now));

        TransactionService service = new(
            transactions,
            categories,
            new FakeUnitOfWork(),
            new FixedClock(Now),
            new RecordingNotifier());

        var draft = new TransactionDraft(
            UserId,
            categoryId,
            -10m,
            TransactionKind.Expense,
            new DateOnly(2026, 3, 10),
            null);

        Result<TransactionItem> result = await service.CreateAsync(UserId, draft);

        Assert.True(result.IsFailure());
        Assert.Empty(await transactions.GetAllAsync(UserId));
    }

    [Fact]
    public async Task CreateAsync_RejectsACategoryOfAnotherType()
    {
        FakeTransactionRepository transactions = new();
        FakeCategoryRepository categories = new();

        Guid categoryId = Guid.NewGuid();
        await categories.AddAsync(new Category(categoryId, UserId, "Food", TransactionKind.Expense, "#FF0000", "cart", false, Now));

        TransactionService service = new(
            transactions,
            categories,
            new FakeUnitOfWork(),
            new FixedClock(Now),
            new RecordingNotifier());

        var draft = new TransactionDraft(
            UserId,
            categoryId,
            10m,
            TransactionKind.Income,
            new DateOnly(2026, 3, 10),
            null);

        Result<TransactionItem> result = await service.CreateAsync(UserId, draft);

        Assert.True(result.IsFailure());
        Assert.Equal("kind_mismatch", ((Fail<TransactionItem>)result).Code);
    }

    [Fact]
    public async Task CreateAsync_RejectsAnUnknownCategory()
    {
        TransactionService service = new(
            new FakeTransactionRepository(),
            new FakeCategoryRepository(),
            new FakeUnitOfWork(),
            new FixedClock(Now),
            new RecordingNotifier());

        var draft = new TransactionDraft(
            UserId,
            Guid.NewGuid(),
            10m,
            TransactionKind.Expense,
            new DateOnly(2026, 3, 10),
            null);

        Result<TransactionItem> result = await service.CreateAsync(UserId, draft);

        Assert.True(result.IsFailure());
        Assert.Equal("not_found", ((Fail<TransactionItem>)result).Code);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheTransaction()
    {
        FakeTransactionRepository transactions = new();
        FakeCategoryRepository categories = new();

        Guid categoryId = Guid.NewGuid();
        await categories.AddAsync(new Category(categoryId, UserId, "Food", TransactionKind.Expense, "#FF0000", "cart", false, Now));

        TransactionService service = new(
            transactions,
            categories,
            new FakeUnitOfWork(),
            new FixedClock(Now),
            new RecordingNotifier());

        var draft = new TransactionDraft(UserId, categoryId, 10m, TransactionKind.Expense, new DateOnly(2026, 3, 10), null);
        Result<TransactionItem> created = await service.CreateAsync(UserId, draft);

        Result result = await service.DeleteAsync(UserId, created.ValueOr(default!).Id);

        Assert.True(result.IsSuccess());
        Assert.Empty(await transactions.GetAllAsync(UserId));
    }

    [Fact]
    public async Task GetListAsync_FiltersAndPaginatesTheResult()
    {
        FakeTransactionRepository transactions = new();
        FakeCategoryRepository categories = new();

        Guid food = Guid.NewGuid();
        Guid rent = Guid.NewGuid();

        await categories.AddAsync(new Category(food, UserId, "Food", TransactionKind.Expense, "#FF0000", "cart", false, Now));
        await categories.AddAsync(new Category(rent, UserId, "Rent", TransactionKind.Expense, "#00FF00", "home", false, Now));

        for (int index = 0; index < 5; index++)
        {
            await transactions.AddAsync(new Transaction(
                Guid.NewGuid(), UserId, food, Money.FromDecimal(10m), TransactionKind.Expense,
                new DateOnly(2026, 3, index + 1), $"food {index}", false, Now));
        }

        await transactions.AddAsync(new Transaction(
            Guid.NewGuid(), UserId, rent, Money.FromDecimal(500m), TransactionKind.Expense,
            new DateOnly(2026, 3, 10), "rent", false, Now));

        TransactionService service = new(
            transactions,
            categories,
            new FakeUnitOfWork(),
            new FixedClock(Now),
            new RecordingNotifier());

        PagedResult<TransactionItem> page = await service.GetListAsync(
            UserId,
            new TransactionFilter(CategoryId: food, PageSize: 2));

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.PageSize);
    }

    [Fact]
    public async Task ContributeAsync_CompletesTheGoalWhenTheTargetIsReached()
    {
        FakeSavingsGoalRepository goals = new();
        SavingsGoalService service = new(goals, new FakeUnitOfWork(), new FixedClock(Now), new RecordingNotifier());

        var draft = new GoalDraft(UserId, "Laptop", 100m, null, null);
        Result<GoalItem> created = await service.CreateAsync(UserId, draft);

        Result<GoalItem> updated = await service.ContributeAsync(UserId, created.ValueOr(default!).Id, 100m);

        Assert.True(updated.IsSuccess());
        Assert.Equal(GoalStatus.Completed, updated.ValueOr(default!).Status);
        Assert.Equal(1d, updated.ValueOr(default!).Ratio);
    }

    [Fact]
    public async Task ContributeAsync_RejectsANonPositiveAmount()
    {
        FakeSavingsGoalRepository goals = new();
        SavingsGoalService service = new(goals, new FakeUnitOfWork(), new FixedClock(Now), new RecordingNotifier());

        Result<GoalItem> created = await service.CreateAsync(UserId, new GoalDraft(UserId, "Laptop", 100m, null, null));
        Result<GoalItem> result = await service.ContributeAsync(UserId, created.ValueOr(default!).Id, 0m);

        Assert.True(result.IsFailure());
    }

    [Fact]
    public async Task CategoryService_RefusesToDeleteACategoryWithTransactions()
    {
        FakeCategoryRepository categories = new();
        FakeTransactionRepository transactions = new();
        CategoryService service = new(
            categories,
            transactions,
            new FakeUnitOfWork(),
            new FixedClock(Now),
            new RecordingNotifier());

        Guid categoryId = Guid.NewGuid();
        await categories.AddAsync(new Category(categoryId, UserId, "Food", TransactionKind.Expense, "#FF0000", "cart", false, Now));
        await transactions.AddAsync(new Transaction(
            Guid.NewGuid(), UserId, categoryId, Money.FromDecimal(10m), TransactionKind.Expense,
            new DateOnly(2026, 3, 1), null, false, Now));

        Result result = await service.DeleteAsync(UserId, categoryId);

        Assert.True(result.IsFailure());
        Assert.Equal("category_in_use", ((Result.Failure)result).Code);
    }

    [Fact]
    public async Task ExportService_WritesACsvFile()
    {
        FakeTransactionRepository transactions = new();
        FakeCategoryRepository categories = new();

        Guid categoryId = Guid.NewGuid();
        await categories.AddAsync(new Category(categoryId, UserId, "Food, snacks", TransactionKind.Expense, "#FF0000", "cart", false, Now));
        await transactions.AddAsync(new Transaction(
            Guid.NewGuid(), UserId, categoryId, Money.FromDecimal(1234.56m), TransactionKind.Expense,
            new DateOnly(2026, 3, 10), "A \"quoted\" note", false, Now));

        ExportService service = new(transactions, categories, new FakeBudgetRepository(), new FixedClock(Now));

        string csv = await service.TransactionsToCsvAsync(UserId);

        Assert.Contains("Date,Type,Category,Amount,Note", csv);
        Assert.Contains("2026-03-10,expense,\"Food, snacks\",1234.56", csv);
        Assert.Contains("\"A \"\"quoted\"\" note\"", csv);
    }

    [Fact]
    public void CsvWriter_EscapesTheSpecialCharacters()
    {
        string csv = CsvWriter.Write(["A", "B"], [new[] { "x,y", "line\nbreak" }]);

        Assert.Contains("\"x,y\"", csv);
        Assert.Contains("\"line\nbreak\"", csv);
    }

    [Fact]
    public async Task ExportService_WritesAJsonFile()
    {
        FakeTransactionRepository transactions = new();
        FakeCategoryRepository categories = new();

        Guid categoryId = Guid.NewGuid();
        await categories.AddAsync(new Category(categoryId, UserId, "Food", TransactionKind.Expense, "#FF0000", "cart", false, Now));
        await transactions.AddAsync(new Transaction(
            Guid.NewGuid(), UserId, categoryId, Money.FromDecimal(10m), TransactionKind.Expense,
            new DateOnly(2026, 3, 10), null, false, Now));

        ExportService service = new(transactions, categories, new FakeBudgetRepository(), new FixedClock(Now));

        string json = await service.TransactionsToJsonAsync(UserId);

        Assert.Contains("\"amount\": 10", json);
        Assert.Contains("\"category\": \"Food\"", json);
    }

    [Fact]
    public void ThePasswordHasher_CreatesADifferentSaltForEveryPassword()
    {
        var hasher = new FinanceFlow.Infrastructure.Security.PasswordHasher();

        (string hash1, string salt1) = hasher.Hash("secret123");
        (string hash2, string salt2) = hasher.Hash("secret123");

        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
        Assert.True(hasher.Verify("secret123", hash1, salt1));
        Assert.False(hasher.Verify("other", hash1, salt1));
    }
}
