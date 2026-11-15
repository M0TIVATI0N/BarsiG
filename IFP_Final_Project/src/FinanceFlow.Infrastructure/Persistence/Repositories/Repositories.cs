using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace FinanceFlow.Infrastructure.Persistence.Repositories;

/// <summary>Stores users in the database.</summary>
public sealed class UserRepository : IUserRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the user repository.</summary>
    public UserRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        string normalized = email.Trim().ToLowerInvariant();

        return _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Email == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public Task<User?> FindByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default) =>
        _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.PhoneNumber == phoneNumber, cancellationToken);

    /// <inheritdoc />
    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        string normalized = email.Trim().ToLowerInvariant();

        return _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Email == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> PhoneNumberExistsAsync(string phoneNumber, CancellationToken cancellationToken = default) =>
        _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.PhoneNumber == phoneNumber, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(User user, CancellationToken cancellationToken = default) =>
        await _dbContext.Users.AddAsync(user, cancellationToken);
}

/// <summary>Stores categories in the database.</summary>
public sealed class CategoryRepository : ICategoryRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the category repository.</summary>
    public CategoryRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Category>> GetAllAsync(
        Guid userId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Category> query = _dbContext.Categories
            .AsNoTracking()
            .Where(category => category.UserId == userId);

        if (!includeArchived)
        {
            query = query.Where(category => !category.IsArchived);
        }

        return await query
            .OrderBy(category => category.Kind)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Category?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(category => category.Id == id && category.UserId == userId, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Category category, CancellationToken cancellationToken = default) =>
        await _dbContext.Categories.AddAsync(category, cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(Category category, CancellationToken cancellationToken = default)
    {
        Category? existing = await _dbContext.Categories
            .FirstOrDefaultAsync(item => item.Id == category.Id && item.UserId == category.UserId, cancellationToken);

        if (existing is null)
        {
            return;
        }

        _dbContext.Entry(existing).CurrentValues.SetValues(category);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        Category? existing = await _dbContext.Categories
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken);

        if (existing is not null)
        {
            _dbContext.Categories.Remove(existing);
        }
    }
}

/// <summary>Stores transactions in the database.</summary>
public sealed class TransactionRepository : ITransactionRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the transaction repository.</summary>
    public TransactionRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Transaction>> GetAllAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId)
            .OrderByDescending(transaction => transaction.OccurredOn)
            .ThenByDescending(transaction => transaction.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Transaction>> GetRangeAsync(
        Guid userId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.UserId == userId &&
                                 transaction.OccurredOn >= from &&
                                 transaction.OccurredOn <= to)
            .OrderByDescending(transaction => transaction.OccurredOn)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Transaction?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(transaction => transaction.Id == id && transaction.UserId == userId, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default) =>
        await _dbContext.Transactions.AddAsync(transaction, cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        Transaction? existing = await _dbContext.Transactions
            .FirstOrDefaultAsync(item => item.Id == transaction.Id && item.UserId == transaction.UserId, cancellationToken);

        if (existing is null)
        {
            return;
        }

        _dbContext.Entry(existing).CurrentValues.SetValues(transaction);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        Transaction? existing = await _dbContext.Transactions
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken);

        if (existing is not null)
        {
            _dbContext.Transactions.Remove(existing);
        }
    }

    /// <inheritdoc />
    public Task<bool> ExistsInMonthAsync(
        Guid userId,
        Guid categoryId,
        int year,
        int month,
        CancellationToken cancellationToken = default) =>
        _dbContext.Transactions
            .AsNoTracking()
            .AnyAsync(
                transaction => transaction.UserId == userId &&
                              transaction.CategoryId == categoryId &&
                              transaction.OccurredOn.Year == year &&
                              transaction.OccurredOn.Month == month,
                cancellationToken);
}

/// <summary>Stores budgets in the database.</summary>
public sealed class BudgetRepository : IBudgetRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the budget repository.</summary>
    public BudgetRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Budget>> GetAllAsync(
        Guid userId,
        int? year = null,
        int? month = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Budget> query = _dbContext.Budgets
            .AsNoTracking()
            .Where(budget => budget.UserId == userId);

        if (year is { } targetYear)
        {
            query = query.Where(budget => budget.Year == targetYear);
        }

        if (month is { } targetMonth)
        {
            query = query.Where(budget => budget.Month == targetMonth);
        }

        return await query
            .OrderBy(budget => budget.Year)
            .ThenBy(budget => budget.Month)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Budget?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.Budgets
            .AsNoTracking()
            .FirstOrDefaultAsync(budget => budget.Id == id && budget.UserId == userId, cancellationToken);

    /// <inheritdoc />
    public Task<Budget?> FindAsync(
        Guid userId,
        Guid categoryId,
        int year,
        int month,
        CancellationToken cancellationToken = default) =>
        _dbContext.Budgets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                budget => budget.UserId == userId &&
                          budget.CategoryId == categoryId &&
                          budget.Year == year &&
                          budget.Month == month,
                cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Budget budget, CancellationToken cancellationToken = default) =>
        await _dbContext.Budgets.AddAsync(budget, cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(Budget budget, CancellationToken cancellationToken = default)
    {
        Budget? existing = await _dbContext.Budgets
            .FirstOrDefaultAsync(item => item.Id == budget.Id && item.UserId == budget.UserId, cancellationToken);

        if (existing is null)
        {
            return;
        }

        _dbContext.Entry(existing).CurrentValues.SetValues(budget);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        Budget? existing = await _dbContext.Budgets
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken);

        if (existing is not null)
        {
            _dbContext.Budgets.Remove(existing);
        }
    }
}

/// <summary>Stores savings goals in the database.</summary>
public sealed class SavingsGoalRepository : ISavingsGoalRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the savings goal repository.</summary>
    public SavingsGoalRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SavingsGoal>> GetAllAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.SavingsGoals
            .AsNoTracking()
            .Where(goal => goal.UserId == userId)
            .OrderBy(goal => goal.Status)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<SavingsGoal?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.SavingsGoals
            .AsNoTracking()
            .FirstOrDefaultAsync(goal => goal.Id == id && goal.UserId == userId, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(SavingsGoal goal, CancellationToken cancellationToken = default) =>
        await _dbContext.SavingsGoals.AddAsync(goal, cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(SavingsGoal goal, CancellationToken cancellationToken = default)
    {
        SavingsGoal? existing = await _dbContext.SavingsGoals
            .FirstOrDefaultAsync(item => item.Id == goal.Id && item.UserId == goal.UserId, cancellationToken);

        if (existing is null)
        {
            return;
        }

        _dbContext.Entry(existing).CurrentValues.SetValues(goal);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        SavingsGoal? existing = await _dbContext.SavingsGoals
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken);

        if (existing is not null)
        {
            _dbContext.SavingsGoals.Remove(existing);
        }
    }
}

/// <summary>Stores recurring rules in the database.</summary>
public sealed class RecurringRuleRepository : IRecurringRuleRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the recurring rule repository.</summary>
    public RecurringRuleRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecurringRule>> GetActiveAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.RecurringRules
            .AsNoTracking()
            .Where(rule => rule.UserId == userId && rule.IsActive)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecurringRule>> GetAllActiveAsync(
        CancellationToken cancellationToken = default) =>
        await _dbContext.RecurringRules
            .AsNoTracking()
            .Where(rule => rule.IsActive)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecurringRule>> GetAllAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.RecurringRules
            .AsNoTracking()
            .Where(rule => rule.UserId == userId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(RecurringRule rule, CancellationToken cancellationToken = default) =>
        await _dbContext.RecurringRules.AddAsync(rule, cancellationToken);

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        RecurringRule? existing = await _dbContext.RecurringRules
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken);

        if (existing is not null)
        {
            _dbContext.RecurringRules.Remove(existing);
        }
    }
}

/// <summary>Saves all pending changes of the persistence context.</summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the unit of work.</summary>
    public UnitOfWork(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
