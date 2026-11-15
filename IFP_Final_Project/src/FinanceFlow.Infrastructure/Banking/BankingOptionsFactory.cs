using FinanceFlow.Application.Banking;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;

using Microsoft.Extensions.Configuration;

namespace FinanceFlow.Infrastructure.Banking;

/// <summary>
/// Builds the rules of the Demo Bank from the configuration of the server.
/// Every value has a safe default, so the application also starts without a configuration file.
/// </summary>
public static class BankingOptionsFactory
{
    /// <summary>The section of the configuration that holds the rules of the bank.</summary>
    public const string SectionName = "Banking";

    /// <summary>Reads the rules of the bank from the configuration.</summary>
    public static BankingOptions Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection(SectionName);

        decimal minimum = section.GetValue("MinimumTransfer", 1m);
        decimal maximum = section.GetValue("MaximumTransfer", 1_000_000m);
        decimal daily = section.GetValue("MaximumDailyTransfer", 2_000_000m);
        decimal interest = section.GetValue("DepositInterestRatePercent", 12m);
        decimal reserve = section.GetValue("InitialReserve", 1_000_000m);
        string environment = configuration["Runtime:Environment"]
            ?? configuration["Desktop:Environment"]
            ?? configuration["ASPNETCORE_ENVIRONMENT"]
            ?? "Production";
        bool allowLocalDemoPhoneRecipients =
            environment.Equals("Development", StringComparison.OrdinalIgnoreCase) &&
            configuration.GetValue("PhoneVerification:EnableLocalDemo", false);

        return new BankingOptions(
            new BankingLimits(
                Money.FromDecimal(minimum < 0m ? 0m : minimum),
                Money.FromDecimal(maximum <= 0m ? 1_000_000m : maximum),
                Money.FromDecimal(daily <= 0m ? 2_000_000m : daily)),
            interest < 0m ? 0m : interest,
            reserve < 0m ? 0m : reserve,
            allowLocalDemoPhoneRecipients);
    }
}
