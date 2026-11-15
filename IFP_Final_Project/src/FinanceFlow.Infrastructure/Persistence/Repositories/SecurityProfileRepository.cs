using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Banking;

using Microsoft.EntityFrameworkCore;

namespace FinanceFlow.Infrastructure.Persistence.Repositories;

/// <summary>Stores the security profiles of the users in the database.</summary>
public sealed class SecurityProfileRepository : ISecurityProfileRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the security profile repository.</summary>
    public SecurityProfileRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<UserSecurityProfile?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        _dbContext.UserSecurityProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(UserSecurityProfile profile, CancellationToken cancellationToken = default) =>
        await _dbContext.UserSecurityProfiles.AddAsync(profile, cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(UserSecurityProfile profile, CancellationToken cancellationToken = default)
    {
        UserSecurityProfile? existing = await _dbContext.UserSecurityProfiles
            .FirstOrDefaultAsync(candidate => candidate.Id == profile.Id, cancellationToken);

        if (existing is null)
        {
            return;
        }

        _dbContext.Entry(existing).CurrentValues.SetValues(profile);
    }
}
