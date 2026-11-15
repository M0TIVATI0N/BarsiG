using FinanceFlow.Application.Banking.Dtos;
using FinanceFlow.Domain.Banking;

namespace FinanceFlow.Application.Abstractions;

/// <summary>Stores and reads the virtual cards of the customers.</summary>
public interface IVirtualCardRepository
{
    /// <summary>Reads one card of a user.</summary>
    Task<VirtualCard?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Reads every card of a user, newest first.</summary>
    Task<IReadOnlyList<VirtualCard>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new card.</summary>
    Task AddAsync(VirtualCard card, CancellationToken cancellationToken = default);

    /// <summary>Writes the changed card back.</summary>
    Task UpdateAsync(VirtualCard card, CancellationToken cancellationToken = default);
}

/// <summary>Renders the receipt of a transfer as a PDF file.</summary>
public interface IReceiptRenderer
{
    /// <summary>Builds the PDF file of a receipt.</summary>
    /// <param name="receipt">The data that has to be printed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<byte[]> RenderAsync(
        TransferReceiptData receipt,
        CancellationToken cancellationToken = default);
}
