using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace FinanceFlow.Infrastructure.Persistence;

/// <summary>
/// The persistence context of the application.
/// It stores the immutable domain records. The records are mapped with constructor binding,
/// so the domain model stays free of any persistence attribute.
/// </summary>
public sealed class FinanceFlowDbContext : DbContext
{
    /// <summary>Creates the persistence context.</summary>
    public FinanceFlowDbContext(DbContextOptions<FinanceFlowDbContext> options)
        : base(options)
    {
    }

    /// <summary>All users.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>All categories.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>All transactions.</summary>
    public DbSet<Transaction> Transactions => Set<Transaction>();

    /// <summary>All budgets.</summary>
    public DbSet<Budget> Budgets => Set<Budget>();

    /// <summary>All savings goals.</summary>
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();

    /// <summary>All recurring rules.</summary>
    public DbSet<RecurringRule> RecurringRules => Set<RecurringRule>();

    /// <summary>All bank accounts.</summary>
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    /// <summary>All ledger transfers.</summary>
    public DbSet<LedgerTransfer> LedgerTransfers => Set<LedgerTransfer>();

    /// <summary>All ledger entries.</summary>
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    /// <summary>All security profiles.</summary>
    public DbSet<UserSecurityProfile> UserSecurityProfiles => Set<UserSecurityProfile>();

    /// <summary>All savings deposits.</summary>
    public DbSet<Deposit> Deposits => Set<Deposit>();

    /// <summary>All monthly interest payments of the deposits.</summary>
    public DbSet<DepositInterestAccrual> DepositInterestAccruals => Set<DepositInterestAccrual>();

    /// <summary>All sessions of the customers.</summary>
    public DbSet<CustomerSession> UserSessions => Set<CustomerSession>();

    /// <summary>All recorded attempts to sign in.</summary>
    public DbSet<LoginEvent> LoginEvents => Set<LoginEvent>();

    /// <summary>All entries of the security audit.</summary>
    public DbSet<SecurityAuditLog> SecurityAuditLogs => Set<SecurityAuditLog>();

    /// <summary>All virtual cards of the customers.</summary>
    public DbSet<VirtualCard> VirtualCards => Set<VirtualCard>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceFlowDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
