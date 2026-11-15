using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Enums;

namespace FinanceFlow.Domain.Entities;

/// <summary>A registered application user. Passwords are stored as a PBKDF2 hash plus a salt.</summary>
public sealed record User(
    Guid Id,
    string Email,
    string DisplayName,
    string PasswordHash,
    string PasswordSalt,
    DateTimeOffset CreatedAtUtc,
    string? PhoneNumber = null,
    bool IsPhoneVerified = false,
    FinanceFlow.Domain.Enums.PhoneVerificationMethod PhoneVerificationMethod =
        FinanceFlow.Domain.Enums.PhoneVerificationMethod.None);

/// <summary>A user defined category of income or expense.</summary>
public sealed record Category(
    Guid Id,
    Guid UserId,
    string Name,
    TransactionKind Kind,
    string ColorHex,
    string Icon,
    bool IsArchived,
    DateTimeOffset CreatedAtUtc);

/// <summary>A single financial operation.</summary>
public sealed record Transaction(
    Guid Id,
    Guid UserId,
    Guid CategoryId,
    Money Amount,
    TransactionKind Kind,
    DateOnly OccurredOn,
    string? Note,
    bool IsRecurring,
    DateTimeOffset CreatedAtUtc);

/// <summary>A spending limit for one category in one calendar month.</summary>
public sealed record Budget(
    Guid Id,
    Guid UserId,
    Guid CategoryId,
    int Year,
    int Month,
    Money Limit,
    DateTimeOffset CreatedAtUtc);

/// <summary>A financial target the user is saving towards.</summary>
public sealed record SavingsGoal(
    Guid Id,
    Guid UserId,
    string Title,
    Money TargetAmount,
    Money SavedAmount,
    DateOnly? TargetDate,
    GoalStatus Status,
    string? Note,
    DateTimeOffset CreatedAtUtc);

/// <summary>A rule that repeats a transaction on a fixed day of every month.</summary>
public sealed record RecurringRule(
    Guid Id,
    Guid UserId,
    Guid CategoryId,
    Money Amount,
    TransactionKind Kind,
    int DayOfMonth,
    string Description,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);
