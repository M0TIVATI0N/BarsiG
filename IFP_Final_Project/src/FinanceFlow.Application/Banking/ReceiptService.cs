using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Banking.Dtos;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;

namespace FinanceFlow.Application.Banking;

/// <summary>
/// Builds the receipt of a transfer.
/// Only the customer who took part in the transfer may read the receipt.
/// </summary>
public sealed class ReceiptService
{
    private readonly ILedgerRepository _ledger;
    private readonly IBankAccountRepository _accounts;
    private readonly IReceiptRenderer _renderer;
    private readonly IUserRepository _users;

    /// <summary>Creates the receipt service.</summary>
    public ReceiptService(
        ILedgerRepository ledger,
        IBankAccountRepository accounts,
        IReceiptRenderer renderer,
        IUserRepository users)
    {
        _ledger = ledger;
        _accounts = accounts;
        _renderer = renderer;
        _users = users;
    }

    /// <summary>Reads the data of the receipt of a transfer.</summary>
    public async Task<Result<TransferReceiptData>> GetDataAsync(
        Guid userId,
        Guid transferId,
        CancellationToken cancellationToken = default)
    {
        LedgerTransfer? transfer = await _ledger.GetTransferAsync(transferId, cancellationToken);

        if (transfer is null)
        {
            return new Fail<TransferReceiptData>("The transaction does not exist.")
            {
                Code = "transfer_not_found"
            };
        }

        bool isParticipant = transfer.RequestedByUserId == userId;

        BankAccount? from = await _accounts.GetByIdAsync(transfer.FromAccountId, cancellationToken);
        BankAccount? to = await _accounts.GetByIdAsync(transfer.ToAccountId, cancellationToken);

        if (from is not null && to is not null)
        {
            isParticipant |= from.UserId == userId || to.UserId == userId;
        }

        if (!isParticipant)
        {
            return new Fail<TransferReceiptData>("The transaction does not exist.")
            {
                Code = "transfer_not_found"
            };
        }

        string senderName = await ResolveNameAsync(from, cancellationToken);
        string recipientName = await ResolveNameAsync(to, cancellationToken);

        return new Ok<TransferReceiptData>(new TransferReceiptData(
            transfer.Id,
            transfer.Kind,
            transfer.Status,
            transfer.Amount,
            transfer.Currency,
            senderName,
            recipientName,
            MaskAccount(from?.AccountNumber),
            MaskAccount(to?.AccountNumber),
            transfer.Description,
            transfer.CreatedAtUtc,
            transfer.CompletedAtUtc));
    }

    /// <summary>Renders the receipt of a transfer as a PDF file.</summary>
    public async Task<Result<byte[]>> RenderAsync(
        Guid userId,
        Guid transferId,
        CancellationToken cancellationToken = default)
    {
        Result<TransferReceiptData> data = await GetDataAsync(userId, transferId, cancellationToken);

        if (data.IsFailure())
        {
            return new Fail<byte[]>(data.Error) { Code = ((Fail<TransferReceiptData>)data).Code };
        }

        byte[] pdf = await _renderer.RenderAsync(data.ValueOr(default!), cancellationToken);

        return new Ok<byte[]>(pdf);
    }

    /// <summary>Finds the name of the owner of an account.</summary>
    private async Task<string> ResolveNameAsync(BankAccount? account, CancellationToken cancellationToken)
    {
        if (account is null)
        {
            return "BarsiG";
        }

        if (account.UserId is null)
        {
            return account.DisplayName;
        }

        FinanceFlow.Domain.Entities.User? user =
            await _users.FindByIdAsync(account.UserId.Value, cancellationToken);

        return user?.DisplayName ?? account.DisplayName;
    }

    /// <summary>Hides the middle part of an account number.</summary>
    public static string MaskAccount(string? accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            return string.Empty;
        }

        string[] parts = accountNumber.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length <= 2)
        {
            return "•••• " + accountNumber[^4..];
        }

        return $"•••• {parts[^1]}";
    }
}
