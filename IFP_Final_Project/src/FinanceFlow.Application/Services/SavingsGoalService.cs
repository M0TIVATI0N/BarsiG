using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Dtos;
using FinanceFlow.Application.Mapping;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Domain.Models;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.Application.Services;

/// <summary>Manages the savings goals of a user.</summary>
public sealed class SavingsGoalService
{
    private readonly ISavingsGoalRepository _goals;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IDataChangeNotifier _notifier;

    /// <summary>Creates the savings goal service.</summary>
    public SavingsGoalService(
        ISavingsGoalRepository goals,
        IUnitOfWork unitOfWork,
        IClock clock,
        IDataChangeNotifier notifier)
    {
        _goals = goals;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _notifier = notifier;
    }

    /// <summary>Returns all savings goals of the user.</summary>
    public async Task<IReadOnlyList<GoalItem>> GetAllAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SavingsGoal> goals = await _goals.GetAllAsync(userId, cancellationToken);

        return goals
            .Select(goal => EntityMapper.ToItem(goal, _clock.Today))
            .OrderBy(goal => goal.Status)
            .ThenByDescending(goal => goal.Ratio)
            .ToList();
    }

    /// <summary>
    /// Creates a savings goal. The draft is validated by pure functions before anything is stored.
    /// </summary>
    public async Task<Result<GoalItem>> CreateAsync(
        Guid userId,
        GoalDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return await GoalValidator.Validate(draft)
            .BindAsync((ValidatedGoal validated) => StoreAsync(userId, validated, cancellationToken));
    }

    /// <summary>Adds money to a savings goal and completes it when the target is reached.</summary>
    public async Task<Result<GoalItem>> ContributeAsync(
        Guid userId,
        Guid goalId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        // The whole method is one pipeline of two fallible steps. The first step checks
        // the amount with a pure function, the second one moves the money. A failure of
        // the first step stops the second one, therefore the money is never added
        // with an invalid amount.
        return await Validation
            .Positive(amount, "Amount", "The contribution must be greater than zero.")
            .Map(static valid => Money.FromDecimal(valid))
            .BindAsync(money => AddMoneyAsync(userId, goalId, money, cancellationToken));
    }

    private async Task<Result<GoalItem>> AddMoneyAsync(
        Guid userId,
        Guid goalId,
        Money contribution,
        CancellationToken cancellationToken)
    {
        SavingsGoal? goal = await _goals.GetByIdAsync(goalId, userId, cancellationToken);
        if (goal is null)
        {
            return new Fail<GoalItem>("The savings goal was not found.") { Code = "not_found" };
        }

        if (goal.Status != GoalStatus.Active)
        {
            return new Fail<GoalItem>("Only an active goal can receive money.") { Code = "goal_closed" };
        }

        Money saved = goal.SavedAmount + contribution;

        SavingsGoal updated = goal with
        {
            SavedAmount = saved,
            Status = saved.Cents >= goal.TargetAmount.Cents ? GoalStatus.Completed : GoalStatus.Active
        };

        await _goals.UpdateAsync(updated, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyAsync(userId, "goals", "updated", cancellationToken);

        return new Ok<GoalItem>(EntityMapper.ToItem(updated, _clock.Today));
    }

    /// <summary>Removes a savings goal.</summary>
    public async Task<Result> DeleteAsync(
        Guid userId,
        Guid goalId,
        CancellationToken cancellationToken = default)
    {
        SavingsGoal? goal = await _goals.GetByIdAsync(goalId, userId, cancellationToken);
        if (goal is null)
        {
            return new Result.Failure("The savings goal was not found.") { Code = "not_found" };
        }

        await _goals.DeleteAsync(goalId, userId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyAsync(userId, "goals", "deleted", cancellationToken);

        return Result.Success.Instance;
    }

    private async Task<Result<GoalItem>> StoreAsync(
        Guid userId,
        ValidatedGoal validated,
        CancellationToken cancellationToken)
    {
        SavingsGoal goal = new(
            Guid.NewGuid(),
            userId,
            validated.Title,
            validated.TargetAmount,
            Money.Zero,
            validated.TargetDate,
            GoalStatus.Active,
            validated.Note,
            _clock.UtcNow);

        await _goals.AddAsync(goal, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyAsync(userId, "goals", "created", cancellationToken);

        return new Ok<GoalItem>(EntityMapper.ToItem(goal, _clock.Today));
    }
}
