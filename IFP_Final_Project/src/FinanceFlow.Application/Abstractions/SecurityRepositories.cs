using FinanceFlow.Domain.Banking;

namespace FinanceFlow.Application.Abstractions;

/// <summary>Stores and reads the sessions of the customers.</summary>
public interface ISessionRepository
{
    /// <summary>Adds a new session.</summary>
    Task AddAsync(CustomerSession session, CancellationToken cancellationToken = default);

    /// <summary>Reads one session of a user.</summary>
    Task<CustomerSession?> GetAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Reads every session of a user, newest first.</summary>
    Task<IReadOnlyList<CustomerSession>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Writes the changed session back.</summary>
    Task UpdateAsync(CustomerSession session, CancellationToken cancellationToken = default);
}

/// <summary>Stores and reads the history of the sign in attempts.</summary>
public interface ILoginEventRepository
{
    /// <summary>Adds a new event.</summary>
    Task AddAsync(LoginEvent loginEvent, CancellationToken cancellationToken = default);

    /// <summary>Reads the events of a user, newest first.</summary>
    Task<IReadOnlyList<LoginEvent>> GetForUserAsync(Guid userId, int take, CancellationToken cancellationToken = default);
}

/// <summary>Stores and reads the security audit.</summary>
public interface ISecurityAuditRepository
{
    /// <summary>Adds a new entry of the audit.</summary>
    Task AddAsync(SecurityAuditLog entry, CancellationToken cancellationToken = default);

    /// <summary>Reads the entries of the audit, newest first.</summary>
    Task<IReadOnlyList<SecurityAuditLog>> GetAsync(int take, CancellationToken cancellationToken = default);
}

/// <summary>
/// Counts the failed attempts to sign in. It is used to block an account for a while
/// after too many wrong passwords.
/// </summary>
public interface ILoginThrottle
{
    /// <summary>Returns <c>true</c> when the account is currently blocked.</summary>
    bool IsBlocked(string key, out TimeSpan remaining);

    /// <summary>Records a failed attempt.</summary>
    void RegisterFailure(string key);

    /// <summary>Removes the failures of the account, the sign in was successful.</summary>
    void RegisterSuccess(string key);
}
