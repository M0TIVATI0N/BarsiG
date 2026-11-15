using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Enums;

namespace FinanceFlow.Domain.Models;

/// <summary>
/// The first stage of the transaction pipeline: raw user input that has not been validated yet.
/// The type makes it impossible to pass unvalidated input into the persistence layer.
/// </summary>
public sealed record TransactionDraft(
    Guid UserId,
    Guid CategoryId,
    decimal Amount,
    TransactionKind Kind,
    DateOnly OccurredOn,
    string? Note);

/// <summary>
/// The second stage of the transaction pipeline: input that passed every pure validation rule.
/// Only a validated transaction can be turned into a stored <see cref="Entities.Transaction"/>.
/// </summary>
public sealed record ValidatedTransaction(
    Guid UserId,
    Guid CategoryId,
    Money Amount,
    TransactionKind Kind,
    DateOnly OccurredOn,
    string? Note);

/// <summary>The first stage of the savings goal pipeline.</summary>
public sealed record GoalDraft(
    Guid UserId,
    string Title,
    decimal TargetAmount,
    DateOnly? TargetDate,
    string? Note);

/// <summary>The second stage of the savings goal pipeline.</summary>
public sealed record ValidatedGoal(
    Guid UserId,
    string Title,
    Money TargetAmount,
    DateOnly? TargetDate,
    string? Note);
