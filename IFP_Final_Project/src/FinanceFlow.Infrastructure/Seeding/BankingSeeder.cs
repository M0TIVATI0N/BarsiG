using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinanceFlow.Infrastructure.Seeding;

/// <summary>
/// Prepares the banking module of an empty or an existing database:
/// the reserve of the Demo Bank, the account that holds the issued money,
/// the primary account of every user and the security profile of every user.
/// </summary>
public static class BankingSeeder
{
    /// <summary>The visible number of the reserve of the Demo Bank.</summary>
    public const string ReserveAccountNumber = "KZ00 0000 0000 0000";

    /// <summary>The visible number of the account that holds the issued money.</summary>
    public const string IssuanceAccountNumber = "KZ00 0000 0000 0001";

    /// <summary>The initial size of the reserve when the configuration does not say anything else.</summary>
    public const decimal DefaultInitialReserve = 1_000_000m;

    /// <summary>The name of the configuration value that holds the initial size of the reserve.</summary>
    public const string InitialReserveSetting = "Banking:InitialReserve";

    /// <summary>Prepares the banking data of the database.</summary>
    /// <param name="services">The service provider of the application.</param>
    /// <param name="configuration">The configuration of the application.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        using IServiceScope scope = services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;

        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BankingSeeder));
        IBankAccountRepository accounts = provider.GetRequiredService<IBankAccountRepository>();
        ISecurityProfileRepository profiles = provider.GetRequiredService<ISecurityProfileRepository>();
        IUserRepository users = provider.GetRequiredService<IUserRepository>();
        IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        IClock clock = provider.GetRequiredService<IClock>();

        await EnsureReserveAsync(provider, accounts, clock, configuration, logger, cancellationToken);

        IReadOnlyList<User> allUsers = await users.GetAllAsync(cancellationToken);

        foreach (User user in allUsers)
        {
            await EnsureSecurityProfileAsync(profiles, unitOfWork, clock, user, cancellationToken);
            await EnsurePrimaryAccountAsync(accounts, unitOfWork, clock, user, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the reserve of the Demo Bank and the counterpart account of the issued money.
    /// The initial amount of the reserve is written as a real ledger movement, so the
    /// conservation of the money holds from the very first row of the database.
    /// </summary>
    private static async Task EnsureReserveAsync(
        IServiceProvider provider,
        IBankAccountRepository accounts,
        IClock clock,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        BankAccount? reserve = await accounts.GetReserveAsync(cancellationToken);

        if (reserve is not null)
        {
            return;
        }

        decimal configured = configuration.GetValue(InitialReserveSetting, DefaultInitialReserve);
        Money amount = Money.FromDecimal(configured < 0m ? 0m : configured);

        DateTimeOffset now = clock.UtcNow;

        BankAccount issuance = new(
            Guid.NewGuid(),
            null,
            IssuanceAccountNumber,
            "BarsiG issued money",
            BankAccountType.Issuance,
            BankAccountStatus.Active,
            -amount,
            "KZT",
            0,
            now,
            null);

        BankAccount reserveAccount = new(
            Guid.NewGuid(),
            null,
            ReserveAccountNumber,
            "Demo Bank reserve",
            BankAccountType.Reserve,
            BankAccountStatus.Active,
            amount,
            "KZT",
            0,
            now,
            null);

        FinanceFlowDbContext dbContext = provider.GetRequiredService<FinanceFlowDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.BankAccounts.AddAsync(issuance, cancellationToken);
        await dbContext.BankAccounts.AddAsync(reserveAccount, cancellationToken);

        LedgerTransfer genesis = new(
            Guid.NewGuid(),
            "genesis",
            LedgerTransferKind.AdminFunding,
            LedgerTransferStatus.Completed,
            issuance.Id,
            reserveAccount.Id,
            null,
            amount,
            "KZT",
            "Initial reserve of the Demo Bank",
            null,
            now,
            now);

        await dbContext.LedgerTransfers.AddAsync(genesis, cancellationToken);

        await dbContext.LedgerEntries.AddAsync(
            new LedgerEntry(
                Guid.NewGuid(),
                genesis.Id,
                issuance.Id,
                LedgerEntryDirection.Debit,
                amount,
                -amount,
                now),
            cancellationToken);

        await dbContext.LedgerEntries.AddAsync(
            new LedgerEntry(
                Guid.NewGuid(),
                genesis.Id,
                reserveAccount.Id,
                LedgerEntryDirection.Credit,
                amount,
                amount,
                now),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("The Demo Bank reserve was created with {Amount}.", amount.Cents);
    }

    /// <summary>Creates the security profile of a user when it does not exist yet.</summary>
    private static async Task EnsureSecurityProfileAsync(
        ISecurityProfileRepository profiles,
        IUnitOfWork unitOfWork,
        IClock clock,
        User user,
        CancellationToken cancellationToken)
    {
        UserSecurityProfile? existing = await profiles.GetByUserIdAsync(user.Id, cancellationToken);

        if (existing is not null)
        {
            return;
        }

        await profiles.AddAsync(
            new UserSecurityProfile(
                Guid.NewGuid(),
                user.Id,
                UserRole.User,
                null,
                null,
                null,
                clock.UtcNow),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Creates the primary account of a user when it does not exist yet.</summary>
    private static async Task EnsurePrimaryAccountAsync(
        IBankAccountRepository accounts,
        IUnitOfWork unitOfWork,
        IClock clock,
        User user,
        CancellationToken cancellationToken)
    {
        BankAccount? existing = await accounts.GetPrimaryAsync(user.Id, cancellationToken);

        if (existing is not null)
        {
            return;
        }

        IReadOnlyList<BankAccount> all = await accounts.GetAllAsync(cancellationToken);

        BankAccount account = new(
            Guid.NewGuid(),
            user.Id,
            BankingMath.BuildAccountNumber(BankingMath.AccountNumberPrefix, all.Count + 1),
            "Primary account",
            BankAccountType.Primary,
            BankAccountStatus.Active,
            Money.Zero,
            "KZT",
            0,
            clock.UtcNow,
            null);

        await accounts.AddAsync(account, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
