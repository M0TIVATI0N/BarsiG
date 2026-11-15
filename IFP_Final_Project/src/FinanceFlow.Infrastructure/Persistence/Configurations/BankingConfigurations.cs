using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinanceFlow.Infrastructure.Persistence.Configurations;

/// <summary>Maps the bank account record to a table.</summary>
public sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.ToTable("BankAccounts");

        builder.HasKey(account => account.Id);

        builder.Property(account => account.AccountNumber).HasMaxLength(32).IsRequired();
        builder.Property(account => account.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(account => account.Type).HasConversion<int>().IsRequired();
        builder.Property(account => account.Status).HasConversion<int>().IsRequired();
        builder.Property(account => account.Currency).HasMaxLength(3).IsRequired();
        builder.Property(account => account.Balance)
            .HasConversion(new Converters.MoneyToCentsConverter())
            .HasColumnName("BalanceCents")
            .IsRequired();
        builder.Property(account => account.CreatedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("CreatedAtUnixMs")
            .IsRequired();
        builder.Property(account => account.ClosedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("ClosedAtUnixMs");

        // The version protects the row against a lost update when two transfers run at the
        // same time. It is increased by the ledger executor in the same transaction.
        builder.Property(account => account.Version).IsConcurrencyToken().IsRequired();

        builder.HasIndex(account => account.AccountNumber).IsUnique();
        builder.HasIndex(account => account.UserId);

        // A user owns exactly one primary account. The filter lets the same user own
        // any number of deposits.
        builder.HasIndex(account => new { account.UserId, account.Type })
            .IsUnique()
            .HasFilter("\"Type\" = 0");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(account => account.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps the ledger transfer record to a table.</summary>
public sealed class LedgerTransferConfiguration : IEntityTypeConfiguration<LedgerTransfer>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LedgerTransfer> builder)
    {
        builder.ToTable("LedgerTransfers");

        builder.HasKey(transfer => transfer.Id);

        builder.Property(transfer => transfer.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(transfer => transfer.Kind).HasConversion<int>().IsRequired();
        builder.Property(transfer => transfer.Status).HasConversion<int>().IsRequired();
        builder.Property(transfer => transfer.Currency).HasMaxLength(3).IsRequired();
        builder.Property(transfer => transfer.Description).HasMaxLength(200).IsRequired();
        builder.Property(transfer => transfer.FailureReason).HasMaxLength(400);
        builder.Property(transfer => transfer.Amount)
            .HasConversion(new Converters.MoneyToCentsConverter())
            .HasColumnName("AmountCents")
            .IsRequired();
        builder.Property(transfer => transfer.CreatedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("CreatedAtUnixMs")
            .IsRequired();
        builder.Property(transfer => transfer.CompletedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("CompletedAtUnixMs");

        // The same key of the same user can be used only once, so a repeated submit
        // can never move the money twice.
        builder.HasIndex(transfer => new { transfer.RequestedByUserId, transfer.IdempotencyKey })
            .IsUnique();

        builder.HasIndex(transfer => transfer.FromAccountId);
        builder.HasIndex(transfer => transfer.ToAccountId);
        builder.HasIndex(transfer => transfer.CreatedAtUtc);

        builder.HasOne<BankAccount>()
            .WithMany()
            .HasForeignKey(transfer => transfer.FromAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BankAccount>()
            .WithMany()
            .HasForeignKey(transfer => transfer.ToAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(transfer => transfer.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps the ledger entry record to a table.</summary>
public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("LedgerEntries");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Direction).HasConversion<int>().IsRequired();
        builder.Property(entry => entry.Amount)
            .HasConversion(new Converters.MoneyToCentsConverter())
            .HasColumnName("AmountCents")
            .IsRequired();
        builder.Property(entry => entry.BalanceAfter)
            .HasConversion(new Converters.MoneyToCentsConverter())
            .HasColumnName("BalanceAfterCents")
            .IsRequired();
        builder.Property(entry => entry.CreatedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("CreatedAtUnixMs")
            .IsRequired();

        // Every transfer has exactly one entry per account, so the pair is unique.
        builder.HasIndex(entry => new { entry.TransferId, entry.AccountId }).IsUnique();
        builder.HasIndex(entry => new { entry.AccountId, entry.CreatedAtUtc });

        builder.HasOne<LedgerTransfer>()
            .WithMany()
            .HasForeignKey(entry => entry.TransferId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BankAccount>()
            .WithMany()
            .HasForeignKey(entry => entry.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps the deposit record to a table.</summary>
public sealed class DepositConfiguration : IEntityTypeConfiguration<Deposit>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Deposit> builder)
    {
        builder.ToTable("Deposits");

        builder.HasKey(deposit => deposit.Id);

        builder.Property(deposit => deposit.Name).HasMaxLength(120).IsRequired();
        builder.Property(deposit => deposit.Status).HasConversion<int>().IsRequired();
        builder.Property(deposit => deposit.AnnualRatePercent).HasColumnType("TEXT").IsRequired();
        builder.Property(deposit => deposit.TermMonths).IsRequired();
        builder.Property(deposit => deposit.LastAccruedMonth)
            .HasColumnName("LastAccruedMonth")
            .IsRequired();
        builder.Property(deposit => deposit.OpenedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("OpenedAtUnixMs")
            .IsRequired();
        builder.Property(deposit => deposit.ClosedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("ClosedAtUnixMs");

        builder.HasIndex(deposit => new { deposit.UserId, deposit.Status });

        builder.HasOne<BankAccount>()
            .WithMany()
            .HasForeignKey(deposit => deposit.BankAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(deposit => deposit.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps the monthly interest payment of a deposit to a table.</summary>
public sealed class DepositInterestAccrualConfiguration : IEntityTypeConfiguration<DepositInterestAccrual>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DepositInterestAccrual> builder)
    {
        builder.ToTable("DepositInterestAccruals");

        builder.HasKey(accrual => accrual.Id);
        builder.Property(accrual => accrual.PeriodMonth).HasColumnName("PeriodMonth").IsRequired();
        builder.Property(accrual => accrual.Interest)
            .HasConversion(new Converters.MoneyToCentsConverter())
            .HasColumnName("InterestCents")
            .IsRequired();
        builder.Property(accrual => accrual.BalanceAfter)
            .HasConversion(new Converters.MoneyToCentsConverter())
            .HasColumnName("BalanceAfterCents")
            .IsRequired();
        builder.Property(accrual => accrual.AccruedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("AccruedAtUnixMs")
            .IsRequired();

        // A month can be paid only once, the database itself prevents a double accrual.
        builder.HasIndex(accrual => new { accrual.DepositId, accrual.PeriodMonth }).IsUnique();

        builder.HasOne<Deposit>()
            .WithMany()
            .HasForeignKey(accrual => accrual.DepositId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<LedgerTransfer>()
            .WithMany()
            .HasForeignKey(accrual => accrual.TransferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps the session of a customer to a table.</summary>
public sealed class UserSessionConfiguration : IEntityTypeConfiguration<CustomerSession>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CustomerSession> builder)
    {
        builder.ToTable("UserSessions");

        builder.HasKey(session => session.Id);

        builder.Property(session => session.IpAddress).HasMaxLength(64).IsRequired();
        builder.Property(session => session.UserAgent).HasMaxLength(400).IsRequired();
        builder.Property(session => session.DeviceName).HasMaxLength(80).IsRequired();
        builder.Property(session => session.CreatedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("CreatedAtUnixMs")
            .IsRequired();
        builder.Property(session => session.LastSeenAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("LastSeenAtUnixMs")
            .IsRequired();
        builder.Property(session => session.RevokedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("RevokedAtUnixMs");

        builder.HasIndex(session => new { session.UserId, session.CreatedAtUtc });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps the history of the sign in attempts to a table.</summary>
public sealed class LoginEventConfiguration : IEntityTypeConfiguration<LoginEvent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LoginEvent> builder)
    {
        builder.ToTable("LoginEvents");

        builder.HasKey(loginEvent => loginEvent.Id);

        builder.Property(loginEvent => loginEvent.EmailHint).HasMaxLength(256).IsRequired();
        builder.Property(loginEvent => loginEvent.Kind).HasConversion<int>().IsRequired();
        builder.Property(loginEvent => loginEvent.IpAddress).HasMaxLength(64).IsRequired();
        builder.Property(loginEvent => loginEvent.DeviceName).HasMaxLength(80).IsRequired();
        builder.Property(loginEvent => loginEvent.Details).HasMaxLength(300).IsRequired();
        builder.Property(loginEvent => loginEvent.OccurredAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("OccurredAtUnixMs")
            .IsRequired();

        builder.HasIndex(loginEvent => new { loginEvent.UserId, loginEvent.OccurredAtUtc });
    }
}

/// <summary>Maps the security audit to a table.</summary>
public sealed class SecurityAuditLogConfiguration : IEntityTypeConfiguration<SecurityAuditLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SecurityAuditLog> builder)
    {
        builder.ToTable("SecurityAuditLogs");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Action).HasConversion<int>().IsRequired();
        builder.Property(entry => entry.Target).HasMaxLength(120).IsRequired();
        builder.Property(entry => entry.IpAddress).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.Details).HasMaxLength(400).IsRequired();
        builder.Property(entry => entry.OccurredAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("OccurredAtUnixMs")
            .IsRequired();

        builder.HasIndex(entry => entry.OccurredAtUtc);
    }
}

/// <summary>Maps the virtual card to a table.</summary>
public sealed class VirtualCardConfiguration : IEntityTypeConfiguration<VirtualCard>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VirtualCard> builder)
    {
        builder.ToTable("VirtualCards");

        builder.HasKey(card => card.Id);

        builder.Property(card => card.Nickname).HasMaxLength(60).IsRequired();
        builder.Property(card => card.MaskedNumber).HasMaxLength(32).IsRequired();
        builder.Property(card => card.LastFour).HasMaxLength(4).IsRequired();
        builder.Property(card => card.Status).HasConversion<int>().IsRequired();
        builder.Property(card => card.DailyLimit)
            .HasConversion(new Converters.MoneyToCentsConverter())
            .HasColumnName("DailyLimitCents")
            .IsRequired();
        builder.Property(card => card.MonthlyLimit)
            .HasConversion(new Converters.MoneyToCentsConverter())
            .HasColumnName("MonthlyLimitCents")
            .IsRequired();
        builder.Property(card => card.CreatedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("CreatedAtUnixMs")
            .IsRequired();
        builder.Property(card => card.FrozenAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("FrozenAtUnixMs");

        builder.HasIndex(card => new { card.UserId, card.Status });

        builder.HasOne<BankAccount>()
            .WithMany()
            .HasForeignKey(card => card.LinkedAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(card => card.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps the security profile of a user to a table.</summary>
public sealed class UserSecurityProfileConfiguration : IEntityTypeConfiguration<UserSecurityProfile>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserSecurityProfile> builder)
    {
        builder.ToTable("UserSecurityProfiles");

        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.Role).HasConversion<int>().IsRequired();
        builder.Property(profile => profile.TransactionPinHash).HasMaxLength(256);
        builder.Property(profile => profile.TransactionPinSalt).HasMaxLength(256);
        builder.Property(profile => profile.TotpSecret).HasMaxLength(64);
        builder.Property(profile => profile.CreatedAtUtc)
            .HasConversion(new Converters.DateTimeOffsetToUnixMillisecondsConverter())
            .HasColumnName("CreatedAtUnixMs")
            .IsRequired();

        builder.HasIndex(profile => profile.UserId).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
