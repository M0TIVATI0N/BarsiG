namespace FinanceFlow.Domain.Banking;

/// <summary>The role of a user inside the Demo Bank.</summary>
public enum UserRole
{
    /// <summary>An ordinary customer. He owns his accounts and may transfer his own money.</summary>
    User = 0,

    /// <summary>
    /// An administrator of the Demo Bank. He may fund the accounts of the users out of the
    /// reserve and read the audit trail. He still never edits a balance directly.
    /// </summary>
    Admin = 1
}

/// <summary>
/// The security profile of a user: the role, the transaction PIN and the optional
/// authenticator secret. It is stored in its own table, so the table of the users
/// stays exactly as it was.
/// </summary>
/// <param name="Id">The technical identifier.</param>
/// <param name="UserId">The owner of the profile.</param>
/// <param name="Role">The role inside the Demo Bank.</param>
/// <param name="TransactionPinHash">The hash of the transaction PIN.</param>
/// <param name="TransactionPinSalt">The salt of the transaction PIN.</param>
/// <param name="TotpSecret">The shared secret of the authenticator, if the user enabled it.</param>
/// <param name="CreatedAtUtc">The moment of the creation.</param>
public sealed record UserSecurityProfile(
    Guid Id,
    Guid UserId,
    UserRole Role,
    string? TransactionPinHash,
    string? TransactionPinSalt,
    string? TotpSecret,
    DateTimeOffset CreatedAtUtc)
{
    /// <summary>Returns <c>true</c> when the user has set a transaction PIN.</summary>
    public bool HasTransactionPin => !string.IsNullOrEmpty(TransactionPinHash);

    /// <summary>Returns <c>true</c> when the user has enabled the authenticator.</summary>
    public bool HasTotp => !string.IsNullOrEmpty(TotpSecret);
}
