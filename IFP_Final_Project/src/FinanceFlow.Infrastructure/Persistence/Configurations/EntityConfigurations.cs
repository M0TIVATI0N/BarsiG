using FinanceFlow.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinanceFlow.Infrastructure.Persistence.Configurations;

/// <summary>Maps the user record to a table.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Email).HasMaxLength(256).IsRequired();
        builder.Property(user => user.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(user => user.PasswordSalt).HasMaxLength(256).IsRequired();
        builder.Property(user => user.PhoneNumber).HasMaxLength(16);
        builder.Property(user => user.PhoneVerificationMethod)
            .HasConversion<int>()
            .HasDefaultValue(FinanceFlow.Domain.Enums.PhoneVerificationMethod.None)
            .IsRequired();
        builder.Property(user => user.CreatedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("CreatedAtUnixMs")
            .IsRequired();

        builder.HasIndex(user => user.Email).IsUnique();
        builder.HasIndex(user => user.PhoneNumber).IsUnique();
    }
}

/// <summary>Maps the category record to a table.</summary>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Name).HasMaxLength(60).IsRequired();
        builder.Property(category => category.ColorHex).HasMaxLength(16).IsRequired();
        builder.Property(category => category.Icon).HasMaxLength(40).IsRequired();
        builder.Property(category => category.Kind).HasConversion<int>().IsRequired();
        builder.Property(category => category.CreatedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("CreatedAtUnixMs")
            .IsRequired();

        builder.HasIndex(category => new { category.UserId, category.Name });
    }
}

/// <summary>Maps the transaction record to a table.</summary>
public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");

        builder.HasKey(transaction => transaction.Id);

        builder.Property(transaction => transaction.Kind).HasConversion<int>().IsRequired();
        builder.Property(transaction => transaction.Note).HasMaxLength(280);
        builder.Property(transaction => transaction.Amount)
            .HasConversion(new Converters.MoneyToCentsConverter())
            .HasColumnName("AmountCents")
            .IsRequired();
        builder.Property(transaction => transaction.CreatedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("CreatedAtUnixMs")
            .IsRequired();

        builder.HasIndex(transaction => new { transaction.UserId, transaction.OccurredOn });
        builder.HasIndex(transaction => new { transaction.UserId, transaction.CategoryId });
    }
}

/// <summary>Maps the budget record to a table.</summary>
public sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("Budgets");

        builder.HasKey(budget => budget.Id);

        builder.Property(budget => budget.Limit)
            .HasConversion(new Converters.MoneyToCentsConverter())
            .HasColumnName("LimitCents")
            .IsRequired();
        builder.Property(budget => budget.CreatedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("CreatedAtUnixMs")
            .IsRequired();

        builder.HasIndex(budget => new { budget.UserId, budget.CategoryId, budget.Year, budget.Month }).IsUnique();
    }
}

/// <summary>Maps the savings goal record to a table.</summary>
public sealed class SavingsGoalConfiguration : IEntityTypeConfiguration<SavingsGoal>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SavingsGoal> builder)
    {
        builder.ToTable("SavingsGoals");

        builder.HasKey(goal => goal.Id);

        builder.Property(goal => goal.Title).HasMaxLength(120).IsRequired();
        builder.Property(goal => goal.Note).HasMaxLength(280);
        builder.Property(goal => goal.Status).HasConversion<int>().IsRequired();
        builder.Property(goal => goal.TargetAmount)
            .HasConversion(new Converters.MoneyToCentsConverter())
            .HasColumnName("TargetAmountCents")
            .IsRequired();
        builder.Property(goal => goal.SavedAmount)
            .HasConversion(new Converters.MoneyToCentsConverter())
            .HasColumnName("SavedAmountCents")
            .IsRequired();
        builder.Property(goal => goal.CreatedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("CreatedAtUnixMs")
            .IsRequired();

        builder.HasIndex(goal => new { goal.UserId, goal.Status });
    }
}

/// <summary>Maps the recurring rule record to a table.</summary>
public sealed class RecurringRuleConfiguration : IEntityTypeConfiguration<RecurringRule>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RecurringRule> builder)
    {
        builder.ToTable("RecurringRules");

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Description).HasMaxLength(160).IsRequired();
        builder.Property(rule => rule.Kind).HasConversion<int>().IsRequired();
        builder.Property(rule => rule.Amount)
            .HasConversion(new Converters.MoneyToCentsConverter())
            .HasColumnName("AmountCents")
            .IsRequired();
        builder.Property(rule => rule.CreatedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("CreatedAtUnixMs")
            .IsRequired();

        builder.HasIndex(rule => new { rule.UserId, rule.IsActive });
    }
}
