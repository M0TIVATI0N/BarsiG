using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Banking;

using Microsoft.EntityFrameworkCore;

namespace FinanceFlow.Infrastructure.Persistence.Repositories;

/// <summary>Stores the savings deposits in the database.</summary>
public sealed class DepositRepository : IDepositRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the deposit repository.</summary>
    public DepositRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<Deposit?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.Deposits
            .AsNoTracking()
            .FirstOrDefaultAsync(deposit => deposit.Id == id && deposit.UserId == userId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Deposit>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Deposits
            .AsNoTracking()
            .Where(deposit => deposit.UserId == userId)
            .OrderByDescending(deposit => deposit.OpenedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Deposit>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Deposits
            .AsNoTracking()
            .Where(deposit => deposit.Status == DepositStatus.Active)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Deposit deposit, CancellationToken cancellationToken = default) =>
        await _dbContext.Deposits.AddAsync(deposit, cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(Deposit deposit, CancellationToken cancellationToken = default)
    {
        Deposit? existing = await _dbContext.Deposits
            .FirstOrDefaultAsync(candidate => candidate.Id == deposit.Id, cancellationToken);

        if (existing is null)
        {
            return;
        }

        _dbContext.Entry(existing).CurrentValues.SetValues(deposit);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Deposit? existing = await _dbContext.Deposits
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (existing is not null)
        {
            _dbContext.Deposits.Remove(existing);
        }
    }

    /// <inheritdoc />
    public async Task AddAccrualAsync(
        DepositInterestAccrual accrual,
        CancellationToken cancellationToken = default) =>
        await _dbContext.DepositInterestAccruals.AddAsync(accrual, cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasAccrualAsync(
        Guid depositId,
        DateOnly month,
        CancellationToken cancellationToken = default) =>
        _dbContext.DepositInterestAccruals
            .AsNoTracking()
            .AnyAsync(accrual => accrual.DepositId == depositId && accrual.PeriodMonth == month, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DepositInterestAccrual>> GetAccrualsAsync(
        Guid depositId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.DepositInterestAccruals
            .AsNoTracking()
            .Where(accrual => accrual.DepositId == depositId)
            .OrderByDescending(accrual => accrual.PeriodMonth)
            .ToListAsync(cancellationToken);
}
