using FinanceFlow.Application.Abstractions;
using FinanceFlow.Infrastructure.Banking;
using FinanceFlow.Infrastructure.Background;
using FinanceFlow.Infrastructure.Export;
using FinanceFlow.Infrastructure.Persistence;
using FinanceFlow.Infrastructure.Persistence.Repositories;
using FinanceFlow.Infrastructure.RealTime;
using FinanceFlow.Infrastructure.Security;
using FinanceFlow.Infrastructure.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceFlow.Infrastructure;

/// <summary>Registers the infrastructure services in the dependency injection container.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>The name of the connection string in the configuration file.</summary>
    public const string ConnectionStringName = "FinanceFlow";

    /// <summary>Adds the database, the repositories and the background services.</summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? "Data Source=financeflow.db";

        services.AddDbContext<FinanceFlowDbContext>(options => options.UseSqlite(connectionString));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<ISavingsGoalRepository, SavingsGoalRepository>();
        services.AddScoped<IRecurringRuleRepository, RecurringRuleRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Banking module: the accounts, the double entry ledger and the atomic executor.
        services.AddScoped<IBankAccountRepository, BankAccountRepository>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<ISecurityProfileRepository, SecurityProfileRepository>();
        services.AddScoped<IDepositRepository, DepositRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<ILoginEventRepository, LoginEventRepository>();
        services.AddScoped<ISecurityAuditRepository, SecurityAuditRepository>();
        services.AddScoped<IVirtualCardRepository, VirtualCardRepository>();
        services.AddSingleton<ILoginThrottle, LoginThrottle>();
        services.AddSingleton<IReceiptRenderer, ReceiptPdfRenderer>();
        services.AddScoped<ILedgerExecutor, LedgerExecutor>();
        services.AddSingleton<LedgerWriteGate>();
        services.AddSingleton(BankingOptionsFactory.Create(configuration));

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IClock, AdjustableClock>();
        services.AddSingleton<AdjustableClock>(provider => (AdjustableClock)provider.GetRequiredService<IClock>());

        services.AddScoped<IDataChangeNotifier, SignalRDataChangeNotifier>();
        services.AddHostedService<RecurringTransactionWorker>();
        services.AddHostedService<DepositInterestWorker>();

        return services;
    }
}
