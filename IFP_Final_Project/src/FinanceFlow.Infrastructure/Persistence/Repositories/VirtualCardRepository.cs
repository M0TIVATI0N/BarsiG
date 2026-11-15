using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Banking;

using Microsoft.EntityFrameworkCore;

namespace FinanceFlow.Infrastructure.Persistence.Repositories;

/// <summary>Stores the virtual cards of the customers in the database.</summary>
public sealed class VirtualCardRepository : IVirtualCardRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the virtual card repository.</summary>
    public VirtualCardRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<VirtualCard?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.VirtualCards
            .AsNoTracking()
            .FirstOrDefaultAsync(card => card.Id == id && card.UserId == userId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<VirtualCard>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.VirtualCards
            .AsNoTracking()
            .Where(card => card.UserId == userId)
            .OrderByDescending(card => card.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(VirtualCard card, CancellationToken cancellationToken = default) =>
        await _dbContext.VirtualCards.AddAsync(card, cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(VirtualCard card, CancellationToken cancellationToken = default)
    {
        VirtualCard? existing = await _dbContext.VirtualCards
            .FirstOrDefaultAsync(candidate => candidate.Id == card.Id, cancellationToken);

        if (existing is null)
        {
            return;
        }

        _dbContext.Entry(existing).CurrentValues.SetValues(card);
    }
}
