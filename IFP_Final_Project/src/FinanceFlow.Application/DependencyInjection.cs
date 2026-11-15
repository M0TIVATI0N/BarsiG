using FinanceFlow.Application.Services;

using Microsoft.Extensions.DependencyInjection;

namespace FinanceFlow.Application;

/// <summary>Registers the application services in the dependency injection container.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Adds every use case of the application layer.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<AuthService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<TransactionService>();
        services.AddScoped<BudgetService>();
        services.AddScoped<SavingsGoalService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<ForecastingService>();
        services.AddScoped<InsightsService>();
        services.AddScoped<ExportService>();

        // Banking module.
        services.AddScoped<FinanceFlow.Application.Banking.AccountService>();
        services.AddScoped<FinanceFlow.Application.Banking.TransferService>();
        services.AddSingleton<FinanceFlow.Application.Banking.PhoneRecipientLookupThrottle>();
        services.AddScoped<FinanceFlow.Application.Banking.AdminService>();
        services.AddScoped<FinanceFlow.Application.Banking.DepositService>();
        services.AddScoped<FinanceFlow.Application.Banking.SecurityService>();
        services.AddScoped<FinanceFlow.Application.Banking.VirtualCardService>();
        services.AddScoped<FinanceFlow.Application.Banking.ReceiptService>();

        return services;
    }
}
