using FinanceFlow.Application.Banking;
using FinanceFlow.Domain.Common;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanceFlow.Infrastructure.Background;

/// <summary>
/// Pays the monthly interest of the deposits.
/// The job is idempotent: a month that was already paid is never paid twice,
/// even when the service runs several times or the server is restarted.
/// </summary>
public sealed class DepositInterestWorker : BackgroundService
{
    /// <summary>The time between two runs of the job.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceProvider _services;
    private readonly ILogger<DepositInterestWorker> _logger;

    /// <summary>Creates the background job.</summary>
    public DepositInterestWorker(IServiceProvider services, ILogger<DepositInterestWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using IServiceScope scope = _services.CreateScope();
                DepositService deposits = scope.ServiceProvider.GetRequiredService<DepositService>();

                var result = await deposits.AccrueInterestAsync(stoppingToken);

                if (result.IsSuccess() && result.ValueOr(0) > 0)
                {
                    _logger.LogInformation("The monthly interest was paid for {Count} deposits.", result.ValueOr(0));
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The interest of the deposits could not be paid.");
            }
        }
    }
}
