using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Entities;

namespace FinanceFlow.UnitTests.Fakes;

/// <summary>An in memory repository of users.</summary>
public sealed class FakeUserRepository : IUserRepository
{
    private readonly List<User> _users = [];

    /// <inheritdoc />
    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        string normalized = email.Trim().ToLowerInvariant();
        return Task.FromResult(_users.FirstOrDefault(user => user.Email == normalized));
    }

    /// <inheritdoc />
    public Task<User?> FindByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.FirstOrDefault(user => user.PhoneNumber == phoneNumber));

    /// <inheritdoc />
    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.FirstOrDefault(user => user.Id == id));

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        string normalized = email.Trim().ToLowerInvariant();
        return Task.FromResult(_users.Any(user => user.Email == normalized));
    }

    /// <inheritdoc />
    public Task<bool> PhoneNumberExistsAsync(string phoneNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Any(user => user.PhoneNumber == phoneNumber));

    /// <inheritdoc />
    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _users.Add(user);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<User>>(_users.ToList());
}

/// <summary>An in memory repository of categories.</summary>
public sealed class FakeCategoryRepository : ICategoryRepository
{
    private readonly List<Category> _categories = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<Category>> GetAllAsync(
        Guid userId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Category>>(_categories
            .Where(category => category.UserId == userId)
            .Where(category => includeArchived || !category.IsArchived)
            .ToList());

    /// <inheritdoc />
    public Task<Category?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_categories.FirstOrDefault(category => category.Id == id && category.UserId == userId));

    /// <inheritdoc />
    public Task AddAsync(Category category, CancellationToken cancellationToken = default)
    {
        _categories.Add(category);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(Category category, CancellationToken cancellationToken = default)
    {
        int index = _categories.FindIndex(item => item.Id == category.Id);

        if (index >= 0)
        {
            _categories[index] = category;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        _categories.RemoveAll(category => category.Id == id && category.UserId == userId);
        return Task.CompletedTask;
    }
}

/// <summary>An in memory repository of transactions.</summary>
public sealed class FakeTransactionRepository : ITransactionRepository
{
    private readonly List<Transaction> _transactions = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<Transaction>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Transaction>>(_transactions
            .Where(transaction => transaction.UserId == userId)
            .OrderByDescending(transaction => transaction.OccurredOn)
            .ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<Transaction>> GetRangeAsync(
        Guid userId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Transaction>>(_transactions
            .Where(transaction => transaction.UserId == userId &&
                                 transaction.OccurredOn >= from &&
                                 transaction.OccurredOn <= to)
            .ToList());

    /// <inheritdoc />
    public Task<Transaction?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_transactions.FirstOrDefault(transaction => transaction.Id == id && transaction.UserId == userId));

    /// <inheritdoc />
    public Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        _transactions.Add(transaction);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        int index = _transactions.FindIndex(item => item.Id == transaction.Id);

        if (index >= 0)
        {
            _transactions[index] = transaction;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        _transactions.RemoveAll(transaction => transaction.Id == id && transaction.UserId == userId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ExistsInMonthAsync(
        Guid userId,
        Guid categoryId,
        int year,
        int month,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_transactions.Any(transaction =>
            transaction.UserId == userId &&
            transaction.CategoryId == categoryId &&
            transaction.OccurredOn.Year == year &&
            transaction.OccurredOn.Month == month));
}

/// <summary>An in memory repository of budgets.</summary>
public sealed class FakeBudgetRepository : IBudgetRepository
{
    private readonly List<Budget> _budgets = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<Budget>> GetAllAsync(
        Guid userId,
        int? year = null,
        int? month = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Budget>>(_budgets
            .Where(budget => budget.UserId == userId)
            .Where(budget => year is null || budget.Year == year)
            .Where(budget => month is null || budget.Month == month)
            .ToList());

    /// <inheritdoc />
    public Task<Budget?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_budgets.FirstOrDefault(budget => budget.Id == id && budget.UserId == userId));

    /// <inheritdoc />
    public Task<Budget?> FindAsync(
        Guid userId,
        Guid categoryId,
        int year,
        int month,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_budgets.FirstOrDefault(budget =>
            budget.UserId == userId &&
            budget.CategoryId == categoryId &&
            budget.Year == year &&
            budget.Month == month));

    /// <inheritdoc />
    public Task AddAsync(Budget budget, CancellationToken cancellationToken = default)
    {
        _budgets.Add(budget);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(Budget budget, CancellationToken cancellationToken = default)
    {
        int index = _budgets.FindIndex(item => item.Id == budget.Id);

        if (index >= 0)
        {
            _budgets[index] = budget;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        _budgets.RemoveAll(budget => budget.Id == id && budget.UserId == userId);
        return Task.CompletedTask;
    }
}

/// <summary>An in memory repository of savings goals.</summary>
public sealed class FakeSavingsGoalRepository : ISavingsGoalRepository
{
    private readonly List<SavingsGoal> _goals = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<SavingsGoal>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SavingsGoal>>(_goals.Where(goal => goal.UserId == userId).ToList());

    /// <inheritdoc />
    public Task<SavingsGoal?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_goals.FirstOrDefault(goal => goal.Id == id && goal.UserId == userId));

    /// <inheritdoc />
    public Task AddAsync(SavingsGoal goal, CancellationToken cancellationToken = default)
    {
        _goals.Add(goal);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(SavingsGoal goal, CancellationToken cancellationToken = default)
    {
        int index = _goals.FindIndex(item => item.Id == goal.Id);

        if (index >= 0)
        {
            _goals[index] = goal;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        _goals.RemoveAll(goal => goal.Id == id && goal.UserId == userId);
        return Task.CompletedTask;
    }
}

/// <summary>A unit of work that only counts the calls.</summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    /// <summary>The number of times the changes were saved.</summary>
    public int SaveCount { get; private set; }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.FromResult(1);
    }
}

/// <summary>A password hasher that is fast enough for the tests.</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    /// <inheritdoc />
    public (string Hash, string Salt) Hash(string password) =>
        (Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"hash:{password}")),
         Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("salt")));

    /// <inheritdoc />
    public bool Verify(string password, string hash, string salt) =>
        hash == Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"hash:{password}"));
}

/// <summary>A clock that always returns the same moment.</summary>
public sealed class FixedClock : IClock
{
    /// <summary>Creates the clock with a fixed moment.</summary>
    public FixedClock(DateTimeOffset now)
    {
        UtcNow = now;
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; }

    /// <inheritdoc />
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}

/// <summary>A notifier that only remembers the notifications.</summary>
public sealed class RecordingNotifier : IDataChangeNotifier
{
    /// <summary>The recorded notifications.</summary>
    public List<(Guid UserId, string Entity, string Action)> Notifications { get; } = [];

    /// <inheritdoc />
    public Task NotifyAsync(Guid userId, string entity, string action, CancellationToken cancellationToken = default)
    {
        Notifications.Add((userId, entity, action));
        return Task.CompletedTask;
    }
}
