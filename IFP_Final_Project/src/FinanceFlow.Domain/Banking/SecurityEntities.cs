namespace FinanceFlow.Domain.Banking;

/// <summary>The kind of a recorded sign in attempt.</summary>
public enum LoginEventKind
{
    /// <summary>The customer signed in with the right credentials.</summary>
    SignInSuccess = 0,

    /// <summary>The credentials were wrong.</summary>
    SignInFailed = 1,

    /// <summary>The sign in was rejected because too many attempts failed.</summary>
    SignInBlocked = 2,

    /// <summary>The customer signed out.</summary>
    SignOut = 3,

    /// <summary>The session of the customer was terminated by the customer.</summary>
    SessionTerminated = 4
}

/// <summary>The kind of an entry of the security audit.</summary>
public enum AuditActionKind
{
    /// <summary>A customer signed in.</summary>
    SignIn = 0,

    /// <summary>A customer signed out.</summary>
    SignOut = 1,

    /// <summary>A transfer was executed.</summary>
    Transfer = 2,

    /// <summary>The administrator funded a customer.</summary>
    AdminFunding = 3,

    /// <summary>The transaction PIN was set or changed.</summary>
    PinChanged = 4,

    /// <summary>The authenticator was enabled or disabled.</summary>
    TotpChanged = 5,

    /// <summary>A session was terminated.</summary>
    SessionTerminated = 6,

    /// <summary>A sensitive operation was confirmed with the PIN or with the password.</summary>
    StepUp = 7,

    /// <summary>A virtual card was frozen or unfrozen.</summary>
    CardStatusChanged = 8,

    /// <summary>An operation was rejected by the security rules.</summary>
    Rejected = 9
}

/// <summary>
/// A session of a customer. The rows are written by the server, so the page of the security
/// really shows the sessions of the customer and not a guess based on the cookies of the browser.
/// </summary>
/// <param name="Id">The identifier of the session. It travels inside the cookie.</param>
/// <param name="UserId">The owner of the session.</param>
/// <param name="CreatedAtUtc">The moment of the sign in.</param>
/// <param name="LastSeenAtUtc">The moment of the last request of the session.</param>
/// <param name="IpAddress">The address of the client.</param>
/// <param name="UserAgent">The browser of the client.</param>
/// <param name="DeviceName">A short name of the device, built from the browser.</param>
/// <param name="RevokedAtUtc">The moment of the termination, if the session was terminated.</param>
public sealed record CustomerSession(
    Guid Id,
    Guid UserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    string IpAddress,
    string UserAgent,
    string DeviceName,
    DateTimeOffset? RevokedAtUtc)
{
    /// <summary>Returns <c>true</c> when the session is still usable.</summary>
    public bool IsActive => RevokedAtUtc is null;
}

/// <summary>
/// One recorded attempt to sign in. It is the history the customer sees on the security page.
/// </summary>
/// <param name="Id">The technical identifier.</param>
/// <param name="UserId">The customer, when the e-mail address is known.</param>
/// <param name="EmailHint">The e-mail address that was used, hidden in the middle.</param>
/// <param name="Kind">The kind of the attempt.</param>
/// <param name="OccurredAtUtc">The moment of the attempt.</param>
/// <param name="IpAddress">The address of the client.</param>
/// <param name="DeviceName">A short name of the device.</param>
/// <param name="Details">An explanation, for example the reason of a rejection.</param>
public sealed record LoginEvent(
    Guid Id,
    Guid? UserId,
    string EmailHint,
    LoginEventKind Kind,
    DateTimeOffset OccurredAtUtc,
    string IpAddress,
    string DeviceName,
    string Details);

/// <summary>
/// One entry of the security audit. The rows are never changed and never deleted.
/// </summary>
/// <param name="Id">The technical identifier.</param>
/// <param name="ActorUserId">The user who performed the action, if there was one.</param>
/// <param name="Action">The kind of the action.</param>
/// <param name="Target">What the action was about.</param>
/// <param name="Succeeded">Whether the action was successful.</param>
/// <param name="OccurredAtUtc">The moment of the action.</param>
/// <param name="IpAddress">The address of the client.</param>
/// <param name="Details">A short description of the action.</param>
public sealed record SecurityAuditLog(
    Guid Id,
    Guid? ActorUserId,
    AuditActionKind Action,
    string Target,
    bool Succeeded,
    DateTimeOffset OccurredAtUtc,
    string IpAddress,
    string Details);
