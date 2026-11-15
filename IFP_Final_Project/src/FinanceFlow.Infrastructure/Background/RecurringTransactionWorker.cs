using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Entities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanceFlow.Infrastructure.Background;

/// <summary>
/// A background service that materialises the recurring rules.
/// Once per hour it checks every active rule and creates the transaction of the current
/// month when it does not exist yet. The work is done in the background, so the user
/// interface stays responsive.
/// </summary>
public sealed class RecurringTransactionWorker : BackgroundService
{
    /// <summary>The interval between two checks.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecurringTransactionWorker> _logger;

    /// <summary>Creates the worker.</summary>
    public RecurringTransactionWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<RecurringTransactionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessAsync(stoppingToken);
        }
    }

    /// <summary>Runs one round of the worker. It is public so that it can be tested directly.</summary>
    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();

            IRecurringRuleRepository rules = scope.ServiceProvider.GetRequiredService<IRecurringRuleRepository>();
            ITransactionRepository transactions = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            IDataChangeNotifier notifier = scope.ServiceProvider.GetRequiredService<IDataChangeNotifier>();
            IClock clock = scope.ServiceProvider.GetRequiredService<IClock>();

            DateOnly today = clock.Today;
            int created = 0;

            IReadOnlyList<RecurringRule> activeRules = await rules.GetAllActiveAsync(cancellationToken);

            foreach (IGrouping<Guid, RecurringRule> userRules in activeRules.GroupBy(rule => rule.UserId))
            {
                foreach (RecurringRule rule in userRules)
                {
                    bool exists = await transactions.ExistsInMonthAsync(
                        userRules.Key,
                        rule.CategoryId,
                        today.Year,
                        today.Month,
                        cancellationToken);

                    if (exists)
                    {
                        continue;
                    }

                    Transaction transaction = new(
                        Guid.NewGuid(),
                        userRules.Key,
                        rule.CategoryId,
                        rule.Amount,
                        rule.Kind,
                        new DateOnly(today.Year, today.Month, Math.Min(rule.DayOfMonth, DateTime.DaysInMonth(today.Year, today.Month))),
                        rule.Description,
                        true,
                        clock.UtcNow);

                    await transactions.AddAsync(transaction, cancellationToken);
                    created++;

                    await notifier.NotifyAsync(userRules.Key, "transactions", "created", cancellationToken);
                }
            }

            if (created > 0)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Created {Count} recurring transactions.", created);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The application is shutting down, nothing has to be reported.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The recurring transaction worker failed.");
        }
    }
}
