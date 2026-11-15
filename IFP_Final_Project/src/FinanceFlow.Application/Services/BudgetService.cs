using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Dtos;
using FinanceFlow.Application.Mapping;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.Application.Services;

/// <summary>Manages the monthly budgets of a user.</summary>
public sealed class BudgetService
{
    private readonly IBudgetRepository _budgets;
    private readonly ICategoryRepository _categories;
    private readonly ITransactionRepository _transactions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IDataChangeNotifier _notifier;

    /// <summary>Creates the budget service.</summary>
    public BudgetService(
        IBudgetRepository budgets,
        ICategoryRepository categories,
        ITransactionRepository transactions,
        IUnitOfWork unitOfWork,
        IClock clock,
        IDataChangeNotifier notifier)
    {
        _budgets = budgets;
        _categories = categories;
        _transactions = transactions;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _notifier = notifier;
    }

    /// <summary>Returns the budgets of one month with the amount already spent.</summary>
    public async Task<IReadOnlyList<BudgetItem>> GetForMonthAsync(
        Guid userId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Budget> budgets = await _budgets.GetAllAsync(userId, year, month, cancellationToken);
        IReadOnlyList<Category> categories = await _categories.GetAllAsync(userId, true, cancellationToken);
        IReadOnlyList<Transaction> transactions = await _transactions.GetRangeAsync(
            userId,
            new DateOnly(year, month, 1),
            new DateOnly(year, month, DateTime.DaysInMonth(year, month)),
            cancellationToken);

        Dictionary<Guid, Category> categoryById = categories.ToDictionary(category => category.Id);

        return budgets
            .Select(budget =>
            {
                Money spent = new(transactions
                    .Where(transaction => transaction.CategoryId == budget.CategoryId)
                    .Sum(transaction => transaction.Amount.Cents));

                return EntityMapper.ToItem(budget, categoryById.GetValueOrDefault(budget.CategoryId), spent);
            })
            .OrderBy(item => item.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Creates or replaces the budget of one category in one month.</summary>
    public async Task<Result<BudgetItem>> SaveAsync(
        Guid userId,
        Guid categoryId,
        int year,
        int month,
        decimal limit,
        CancellationToken cancellationToken = default)
    {
        Result<decimal> validLimit = Validation.Positive(limit, "Limit", "The limit must be greater than zero.");

        if (validLimit is Fail<decimal> invalidLimit)
        {
            return new Fail<BudgetItem>(invalidLimit.Error)
            {
                Code = invalidLimit.Code,
                Details = invalidLimit.Details
            };
        }

        Category? category = await _categories.GetByIdAsync(categoryId, userId, cancellationToken);
        if (category is null)
        {
            return new Fail<BudgetItem>("The selected category does not exist.") { Code = "not_found" };
        }

        if (category.Kind != TransactionKind.Expense)
        {
            return new Fail<BudgetItem>("A budget can only be set for an expense category.")
            {
                Code = "invalid_category"
            };
        }

        Money limitValue = Money.FromDecimal(limit);
        Budget? existing = await _budgets.FindAsync(userId, categoryId, year, month, cancellationToken);

        Budget budget = existing is null
            ? new Budget(Guid.NewGuid(), userId, categoryId, year, month, limitValue, _clock.UtcNow)
            : existing with { Limit = limitValue };

        if (existing is null)
        {
            await _budgets.AddAsync(budget, cancellationToken);
        }
        else
        {
            await _budgets.UpdateAsync(budget, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyAsync(userId, "budgets", "saved", cancellationToken);

        IReadOnlyList<Transaction> transactions = await _transactions.GetRangeAsync(
            userId,
            new DateOnly(year, month, 1),
            new DateOnly(year, month, DateTime.DaysInMonth(year, month)),
            cancellationToken);

        Money spent = new(transactions
            .Where(transaction => transaction.CategoryId == categoryId)
            .Sum(transaction => transaction.Amount.Cents));

        return new Ok<BudgetItem>(EntityMapper.ToItem(budget, category, spent));
    }

    /// <summary>Removes a budget.</summary>
    public async Task<Result> DeleteAsync(
        Guid userId,
        Guid budgetId,
        CancellationToken cancellationToken = default)
    {
        Budget? budget = await _budgets.GetByIdAsync(budgetId, userId, cancellationToken);
        if (budget is null)
        {
            return new Result.Failure("The budget was not found.") { Code = "not_found" };
        }

        await _budgets.DeleteAsync(budgetId, userId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyAsync(userId, "budgets", "deleted", cancellationToken);

        return Result.Success.Instance;
    }
}
