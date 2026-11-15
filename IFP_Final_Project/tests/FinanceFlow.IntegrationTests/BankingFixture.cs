using FinanceFlow.Application;
using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Infrastructure;
using FinanceFlow.Infrastructure.Persistence;

using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinanceFlow.IntegrationTests;

/// <summary>
/// A real SQLite database that lives only for the duration of one test.
/// It proves that the rules of the bank hold against a real database and not only
/// against an in memory fake.
/// </summary>
public sealed class BankingFixture : IDisposable
{
    private readonly string _databasePath;

    /// <summary>Creates the fixture and applies the migrations.</summary>
    public BankingFixture(
        bool allowLocalDemoPhoneTransfers = false,
        decimal initialReserve = 1_000_000m)
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"barsig-test-{Guid.NewGuid():N}.db");

        Dictionary<string, string?> settings = new()
        {
            ["ConnectionStrings:FinanceFlow"] = $"Data Source={_databasePath}",
            ["Banking:InitialReserve"] = initialReserve.ToString(CultureInfo.InvariantCulture)
        };

        if (allowLocalDemoPhoneTransfers)
        {
            settings["Runtime:Environment"] = "Development";
            settings["PhoneVerification:EnableLocalDemo"] = "true";
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        ServiceCollection collection = new();

        collection.AddApplication();
        collection.AddInfrastructure(configuration);
        collection.AddSignalR();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        Services = collection.BuildServiceProvider();

        using IServiceScope scope = Services.CreateScope();
        FinanceFlowDbContext dbContext = scope.ServiceProvider.GetRequiredService<FinanceFlowDbContext>();
        dbContext.Database.MigrateAsync().GetAwaiter().GetResult();

        Infrastructure.Seeding.BankingSeeder
            .SeedAsync(Services, configuration)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>The services of the application.</summary>
    public ServiceProvider Services { get; }

    /// <summary>Runs an action inside a scope of the application.</summary>
    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        return await action(scope.ServiceProvider);
    }

    /// <summary>Runs an action inside a scope of the application.</summary>
    public async Task RunAsync(Func<IServiceProvider, Task> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        await action(scope.ServiceProvider);
    }

    /// <summary>Creates a user with one primary account that holds exactly zero.</summary>
    public async Task<BankAccount> CreateCustomerAsync(string email, string displayName)
    {
        return await RunAsync(async provider =>
        {
            IUserRepository users = provider.GetRequiredService<IUserRepository>();
            IPasswordHasher hasher = provider.GetRequiredService<IPasswordHasher>();
            IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();
            FinanceFlow.Application.Banking.AccountService accounts =
                provider.GetRequiredService<FinanceFlow.Application.Banking.AccountService>();

            (string hash, string salt) = hasher.Hash("Secret123");

            User user = new(Guid.NewGuid(), email, displayName, hash, salt, DateTimeOffset.UtcNow);
            await users.AddAsync(user);
            await unitOfWork.SaveChangesAsync();

            await accounts.EnsurePrimaryAccountAsync(user.Id);

            BankAccount? primary = await provider
                .GetRequiredService<IBankAccountRepository>()
                .GetPrimaryAsync(user.Id);

            return primary ?? throw new InvalidOperationException("The primary account was not created.");
        });
    }

    /// <summary>Creates a customer with a persisted verified phone identity.</summary>
    public async Task<BankAccount> CreateVerifiedPhoneCustomerAsync(
        string email,
        string displayName,
        string phoneNumber,
        PhoneVerificationMethod verificationMethod = PhoneVerificationMethod.LocalDemo)
    {
        return await RunAsync(async provider =>
        {
            IUserRepository users = provider.GetRequiredService<IUserRepository>();
            IPasswordHasher hasher = provider.GetRequiredService<IPasswordHasher>();
            IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();
            FinanceFlow.Application.Banking.AccountService accounts =
                provider.GetRequiredService<FinanceFlow.Application.Banking.AccountService>();

            (string hash, string salt) = hasher.Hash("Secret123");
            User user = new(
                Guid.NewGuid(),
                email,
                displayName,
                hash,
                salt,
                DateTimeOffset.UtcNow,
                phoneNumber,
                IsPhoneVerified: true,
                PhoneVerificationMethod: verificationMethod);

            await users.AddAsync(user);
            await unitOfWork.SaveChangesAsync();
            await accounts.EnsurePrimaryAccountAsync(user.Id);

            return await provider
                .GetRequiredService<IBankAccountRepository>()
                .GetPrimaryAsync(user.Id)
                ?? throw new InvalidOperationException("The primary account was not created.");
        });
    }

    /// <summary>
    /// Gives virtual money to an account out of the reserve of the Demo Bank.
    /// It is the same movement the administrator of the Demo Bank performs.
    /// </summary>
    public async Task FundAsync(BankAccount account, decimal amount)
    {
        await RunAsync(async provider =>
        {
            ILedgerExecutor executor = provider.GetRequiredService<ILedgerExecutor>();
            IBankAccountRepository accounts = provider.GetRequiredService<IBankAccountRepository>();

            BankAccount reserve = await accounts.GetReserveAsync()
                ?? throw new InvalidOperationException("The reserve does not exist.");

            Result<LedgerTransfer> result = await executor.ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.AdminFunding,
                reserve.Id,
                account.Id,
                account.UserId ?? Guid.Empty,
                Money.FromDecimal(amount),
                "Funding of the test",
                Guid.NewGuid().ToString()));

            if (result.IsFailure())
            {
                throw new InvalidOperationException(result.Error);
            }
        });
    }

    /// <summary>Reads the balance of an account directly from the database.</summary>
    public async Task<Money> ReadBalanceAsync(Guid accountId)
    {
        return await RunAsync(async provider =>
        {
            FinanceFlowDbContext dbContext = provider.GetRequiredService<FinanceFlowDbContext>();

            BankAccount? account = await dbContext.BankAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == accountId);

            return account?.Balance ?? Money.Zero;
        });
    }

    /// <summary>Reads the signed sum of all balances of the database.</summary>
    public async Task<long> ReadTotalSupplyAsync()
    {
        return await RunAsync(async provider =>
        {
            FinanceFlowDbContext dbContext = provider.GetRequiredService<FinanceFlowDbContext>();

            long[] amounts = await dbContext.BankAccounts
                .AsNoTracking()
                .Select(account => account.Balance.Cents)
                .ToArrayAsync();

            return amounts.Sum();
        });
    }

    /// <summary>Releases the database file.</summary>
    public void Dispose()
    {
        Services.Dispose();

        // The connections of the pool still hold the file, they have to be closed first.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch (IOException)
        {
            // The file is removed by the operating system later, it does not break the test.
        }
    }
}
