using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Banking;

using Microsoft.EntityFrameworkCore;

namespace FinanceFlow.Infrastructure.Persistence.Repositories;

/// <summary>Stores the sessions of the customers in the database.</summary>
public sealed class SessionRepository : ISessionRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the session repository.</summary>
    public SessionRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task AddAsync(CustomerSession session, CancellationToken cancellationToken = default) =>
        await _dbContext.UserSessions.AddAsync(session, cancellationToken);

    /// <inheritdoc />
    public Task<CustomerSession?> GetAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.UserSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(session => session.Id == sessionId && session.UserId == userId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerSession>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.UserSessions
            .AsNoTracking()
            .Where(session => session.UserId == userId)
            .OrderByDescending(session => session.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(CustomerSession session, CancellationToken cancellationToken = default)
    {
        CustomerSession? existing = await _dbContext.UserSessions
            .FirstOrDefaultAsync(candidate => candidate.Id == session.Id, cancellationToken);

        if (existing is null)
        {
            return;
        }

        _dbContext.Entry(existing).CurrentValues.SetValues(session);
    }
}

/// <summary>Stores the history of the sign in attempts in the database.</summary>
public sealed class LoginEventRepository : ILoginEventRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the login event repository.</summary>
    public LoginEventRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task AddAsync(LoginEvent loginEvent, CancellationToken cancellationToken = default) =>
        await _dbContext.LoginEvents.AddAsync(loginEvent, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoginEvent>> GetForUserAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken = default) =>
        await _dbContext.LoginEvents
            .AsNoTracking()
            .Where(loginEvent => loginEvent.UserId == userId)
            .OrderByDescending(loginEvent => loginEvent.OccurredAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
}

/// <summary>Stores the security audit in the database.</summary>
public sealed class SecurityAuditRepository : ISecurityAuditRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the audit repository.</summary>
    public SecurityAuditRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task AddAsync(SecurityAuditLog entry, CancellationToken cancellationToken = default) =>
        await _dbContext.SecurityAuditLogs.AddAsync(entry, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SecurityAuditLog>> GetAsync(
        int take,
        CancellationToken cancellationToken = default) =>
        await _dbContext.SecurityAuditLogs
            .AsNoTracking()
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
}
