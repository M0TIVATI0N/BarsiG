using FinanceFlow.Domain.Common;

namespace FinanceFlow.Domain.Banking;

/// <summary>The lifecycle state of a virtual card.</summary>
public enum VirtualCardStatus
{
    /// <summary>The card can be used.</summary>
    Active = 0,

    /// <summary>The card is frozen, it can be used again after a thaw.</summary>
    Frozen = 1,

    /// <summary>The card is not valid any more.</summary>
    Expired = 2
}

/// <summary>
/// A fictional bank card of the Demo Bank.
/// The card is not a real payment instrument: only the last four digits of the fictional
/// number are stored, and they are never used to reach a payment network.
/// </summary>
/// <param name="Id">The technical identifier.</param>
/// <param name="UserId">The owner of the card.</param>
/// <param name="LinkedAccountId">The account the card belongs to.</param>
/// <param name="Nickname">The name the customer gave to the card.</param>
/// <param name="MaskedNumber">The number of the card, only the last four digits are visible.</param>
/// <param name="LastFour">The last four digits of the fictional number.</param>
/// <param name="Status">The lifecycle state of the card.</param>
/// <param name="DailyLimit">The limit of one day, zero means no limit.</param>
/// <param name="MonthlyLimit">The limit of one month, zero means no limit.</param>
/// <param name="CreatedAtUtc">The moment of the creation.</param>
/// <param name="FrozenAtUtc">The moment of the last freezing.</param>
public sealed record VirtualCard(
    Guid Id,
    Guid UserId,
    Guid LinkedAccountId,
    string Nickname,
    string MaskedNumber,
    string LastFour,
    VirtualCardStatus Status,
    Money DailyLimit,
    Money MonthlyLimit,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? FrozenAtUtc)
{
    /// <summary>Returns <c>true</c> when the card can be used.</summary>
    public bool IsUsable => Status == VirtualCardStatus.Active;
}
