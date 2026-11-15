using FinanceFlow.Domain.Entities;

namespace FinanceFlow.Application.Abstractions;

/// <summary>Stores and reads application users.</summary>
public interface IUserRepository
{
    /// <summary>Finds a user by e-mail address, ignoring the case.</summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Finds a user by their canonical E.164 phone number.</summary>
    Task<User?> FindByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Finds a user by identifier.</summary>
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> when an account with this e-mail already exists.</summary>
    Task<bool> ExistsAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Returns true when a phone number is already assigned to an account.</summary>
    Task<bool> PhoneNumberExistsAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Returns every user of the application. It is used by the seeders.</summary>
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a new user.</summary>
    Task AddAsync(User user, CancellationToken cancellationToken = default);
}

/// <summary>Stores and reads income and expense categories.</summary>
public interface ICategoryRepository
{
    /// <summary>Returns all categories of a user.</summary>
    Task<IReadOnlyList<Category>> GetAllAsync(
        Guid userId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one category of a user.</summary>
    Task<Category?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Adds a category.</summary>
    Task AddAsync(Category category, CancellationToken cancellationToken = default);

    /// <summary>Replaces an existing category.</summary>
    Task UpdateAsync(Category category, CancellationToken cancellationToken = default);

    /// <summary>Removes a category.</summary>
    Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Stores and reads financial transactions.</summary>
public interface ITransactionRepository
{
    /// <summary>Returns every transaction of a user, newest first.</summary>
    Task<IReadOnlyList<Transaction>> GetAllAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the transactions of a user inside a date range.</summary>
    Task<IReadOnlyList<Transaction>> GetRangeAsync(
        Guid userId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one transaction of a user.</summary>
    Task<Transaction?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Adds a transaction.</summary>
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default);

    /// <summary>Replaces an existing transaction.</summary>
    Task UpdateAsync(Transaction transaction, CancellationToken cancellationToken = default);

    /// <summary>Removes a transaction.</summary>
    Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> when a transaction of this category already exists in the month.</summary>
    Task<bool> ExistsInMonthAsync(
        Guid userId,
        Guid categoryId,
        int year,
        int month,
        CancellationToken cancellationToken = default);
}

/// <summary>Stores and reads monthly budgets.</summary>
public interface IBudgetRepository
{
    /// <summary>Returns the budgets of a user, optionally limited to one month.</summary>
    Task<IReadOnlyList<Budget>> GetAllAsync(
        Guid userId,
        int? year = null,
        int? month = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one budget of a user.</summary>
    Task<Budget?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Returns the budget of one category in one month.</summary>
    Task<Budget?> FindAsync(
        Guid userId,
        Guid categoryId,
        int year,
        int month,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a budget.</summary>
    Task AddAsync(Budget budget, CancellationToken cancellationToken = default);

    /// <summary>Replaces an existing budget.</summary>
    Task UpdateAsync(Budget budget, CancellationToken cancellationToken = default);

    /// <summary>Removes a budget.</summary>
    Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Stores and reads savings goals.</summary>
public interface ISavingsGoalRepository
{
    /// <summary>Returns all savings goals of a user.</summary>
    Task<IReadOnlyList<SavingsGoal>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Returns one savings goal of a user.</summary>
    Task<SavingsGoal?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Adds a savings goal.</summary>
    Task AddAsync(SavingsGoal goal, CancellationToken cancellationToken = default);

    /// <summary>Replaces an existing savings goal.</summary>
    Task UpdateAsync(SavingsGoal goal, CancellationToken cancellationToken = default);

    /// <summary>Removes a savings goal.</summary>
    Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Stores and reads rules that repeat a transaction every month.</summary>
public interface IRecurringRuleRepository
{
    /// <summary>Returns the active rules of a user.</summary>
    Task<IReadOnlyList<RecurringRule>> GetActiveAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Returns every active rule of every user. Used by the background worker.</summary>
    Task<IReadOnlyList<RecurringRule>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns every rule of a user.</summary>
    Task<IReadOnlyList<RecurringRule>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Adds a rule.</summary>
    Task AddAsync(RecurringRule rule, CancellationToken cancellationToken = default);

    /// <summary>Removes a rule.</summary>
    Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Saves all pending changes of the persistence context in one transaction.</summary>
public interface IUnitOfWork
{
    /// <summary>Persists the pending changes and returns the number of affected rows.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Hashes and verifies passwords.</summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a password and returns the salt that was used.</summary>
    (string Hash, string Salt) Hash(string password);

    /// <summary>Verifies a password against a stored hash and salt.</summary>
    bool Verify(string password, string hash, string salt);
}

/// <summary>Provides the current time, which keeps the services testable.</summary>
public interface IClock
{
    /// <summary>The current moment in UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Today according to the server clock.</summary>
    DateOnly Today { get; }
}

/// <summary>Publishes a change of the data to the connected clients in real time.</summary>
public interface IDataChangeNotifier
{
    /// <summary>Notifies the clients of one user that a piece of data changed.</summary>
    Task NotifyAsync(Guid userId, string entity, string action, CancellationToken cancellationToken = default);
}
