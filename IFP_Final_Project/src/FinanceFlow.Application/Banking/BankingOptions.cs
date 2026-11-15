using FinanceFlow.Domain.Banking;

namespace FinanceFlow.Application.Banking;

/// <summary>
/// The rules of the Demo Bank that the administrator can change in the configuration file.
/// The object is built once by the composition root and never changes while the server runs,
/// so every request of the bank works with exactly the same rules.
/// </summary>
/// <param name="Limits">The limits of a single transfer and of one day.</param>
/// <param name="DepositInterestRatePercent">The yearly interest rate of a deposit, in percent.</param>
/// <param name="InitialReserve">The initial size of the reserve of the Demo Bank.</param>
/// <param name="AllowLocalDemoPhoneRecipients">Whether Development-only local-demo phone identities may receive transfers.</param>
public sealed record BankingOptions(
    BankingLimits Limits,
    decimal DepositInterestRatePercent,
    decimal InitialReserve,
    bool AllowLocalDemoPhoneRecipients = false)
{
    /// <summary>The rules that are used when the configuration does not say anything else.</summary>
    public static BankingOptions Default { get; } = new(BankingLimits.Default, 12m, 1_000_000m);
}
