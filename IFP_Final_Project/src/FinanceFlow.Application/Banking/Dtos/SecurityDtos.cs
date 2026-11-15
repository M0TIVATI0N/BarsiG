namespace FinanceFlow.Application.Banking.Dtos;

/// <summary>The security page of a customer.</summary>
/// <param name="Sessions">The sessions of the customer.</param>
/// <param name="LoginEvents">The history of the sign ins.</param>
/// <param name="HasTransactionPin">Whether the customer set a transaction PIN.</param>
/// <param name="HasTotp">Whether the customer enabled the authenticator.</param>
public sealed record SecurityOverview(
    IReadOnlyList<SessionItem> Sessions,
    IReadOnlyList<LoginEventItem> LoginEvents,
    bool HasTransactionPin,
    bool HasTotp);

/// <summary>One session of a customer.</summary>
/// <param name="Id">The identifier of the session.</param>
/// <param name="DeviceName">The device of the session.</param>
/// <param name="IpAddress">The address of the client.</param>
/// <param name="CreatedAtUtc">The moment of the sign in.</param>
/// <param name="LastSeenAtUtc">The moment of the last request.</param>
/// <param name="IsActive">Whether the session can still be used.</param>
public sealed record SessionItem(
    Guid Id,
    string DeviceName,
    string IpAddress,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    bool IsActive);

/// <summary>One entry of the history of the sign ins.</summary>
/// <param name="Kind">The kind of the attempt.</param>
/// <param name="OccurredAtUtc">The moment of the attempt.</param>
/// <param name="IpAddress">The address of the client.</param>
/// <param name="DeviceName">The device of the client.</param>
/// <param name="Details">An explanation of the attempt.</param>
public sealed record LoginEventItem(
    FinanceFlow.Domain.Banking.LoginEventKind Kind,
    DateTimeOffset OccurredAtUtc,
    string IpAddress,
    string DeviceName,
    string Details);

/// <summary>What a customer has to do to confirm a sensitive operation.</summary>
/// <param name="RequiresPin">Whether the transaction PIN is needed.</param>
/// <param name="RequiresPassword">Whether the password is needed.</param>
/// <param name="Reason">The explanation shown to the customer.</param>
public sealed record StepUpRequirement(bool RequiresPin, bool RequiresPassword, string Reason);
