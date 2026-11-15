using FinanceFlow.Domain.Banking;

namespace FinanceFlow.Application.Abstractions;

/// <summary>Stores and reads the savings deposits of the customers.</summary>
public interface IDepositRepository
{
    /// <summary>Reads one deposit of a user.</summary>
    Task<Deposit?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Reads every deposit of a user.</summary>
    Task<IReadOnlyList<Deposit>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Reads every deposit that is still open.</summary>
    Task<IReadOnlyList<Deposit>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a new deposit.</summary>
    Task AddAsync(Deposit deposit, CancellationToken cancellationToken = default);

    /// <summary>Writes the changed deposit back.</summary>
    Task UpdateAsync(Deposit deposit, CancellationToken cancellationToken = default);

    /// <summary>Removes a deposit that was opened but never funded.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Records a monthly interest payment.</summary>
    Task AddAccrualAsync(DepositInterestAccrual accrual, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> when the deposit already received the interest of the month.</summary>
    Task<bool> HasAccrualAsync(Guid depositId, DateOnly month, CancellationToken cancellationToken = default);

    /// <summary>Reads the interest payments of a deposit, newest first.</summary>
    Task<IReadOnlyList<DepositInterestAccrual>> GetAccrualsAsync(
        Guid depositId,
        CancellationToken cancellationToken = default);
}
