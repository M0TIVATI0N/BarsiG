using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Banking;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceFlow.Infrastructure.Banking;

/// <summary>
/// A gate that lets only one ledger write at a time inside the process.
/// It complements the transaction of the database: the transaction protects the data,
/// the gate protects the read, the check and the write of a movement against each other.
/// </summary>
public sealed class LedgerWriteGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Waits for the permission to write to the ledger.</summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);

        return new Releaser(_semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _semaphore.Release();
        }
    }
}

/// <summary>
/// Executes a money movement inside one database transaction.
/// The transfer, the two ledger entries and the two new balances are written together,
/// so money can never appear or disappear because of a partial failure.
/// </summary>
public sealed class LedgerExecutor : ILedgerExecutor
{
    private readonly FinanceFlowDbContext _dbContext;
    private readonly ILedgerRepository _ledger;
    private readonly LedgerWriteGate _gate;
    private readonly BankingOptions _bankingOptions;
    private readonly IClock _clock;
    private readonly ILogger<LedgerExecutor> _logger;

    /// <summary>Creates the ledger executor.</summary>
    public LedgerExecutor(
        FinanceFlowDbContext dbContext,
        ILedgerRepository ledger,
        LedgerWriteGate gate,
        BankingOptions bankingOptions,
        IClock clock,
        ILogger<LedgerExecutor> logger)
    {
        _dbContext = dbContext;
        _ledger = ledger;
        _gate = gate;
        _bankingOptions = bankingOptions;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<LedgerTransfer>> ExecuteAsync(
        LedgerExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using IDisposable _ = await _gate.AcquireAsync(cancellationToken);

        LedgerTransfer? replay = await _dbContext.LedgerTransfers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                transfer => transfer.RequestedByUserId == request.RequestedByUserId &&
                           transfer.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);

        if (replay is not null)
        {
            // A key is bound to one payload. Replaying that payload is safe; changing it
            // must not silently return a receipt for a different operation.
            if (replay.Kind != request.Kind ||
                replay.FromAccountId != request.FromAccountId ||
                replay.ToAccountId != request.ToAccountId ||
                replay.RequestedByUserId != request.RequestedByUserId ||
                replay.Amount != request.Amount ||
                !string.Equals(replay.Description, request.Description, StringComparison.Ordinal))
            {
                return new Fail<LedgerTransfer>("This idempotency key was already used for a different transfer.")
                {
                    Code = "idempotency_conflict"
                };
            }

            return new Ok<LedgerTransfer>(replay);
        }

        if (request.Amount.IsNegative || request.Amount.IsZero)
        {
            return new Fail<LedgerTransfer>("The amount must be greater than zero.")
            {
                Code = "invalid_amount"
            };
        }

        if (request.FromAccountId == request.ToAccountId)
        {
            return new Fail<LedgerTransfer>("The source and the target account must be different.")
            {
                Code = "same_account"
            };
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            BankAccount? source = await _dbContext.BankAccounts
                .FirstOrDefaultAsync(account => account.Id == request.FromAccountId, cancellationToken);

            BankAccount? target = await _dbContext.BankAccounts
                .FirstOrDefaultAsync(account => account.Id == request.ToAccountId, cancellationToken);

            if (source is null || target is null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return new Fail<LedgerTransfer>("The account does not exist.")
                {
                    Code = "account_not_found"
                };
            }

            if (source.Status != BankAccountStatus.Active || target.Status != BankAccountStatus.Active)
            {
                await transaction.RollbackAsync(cancellationToken);

                return new Fail<LedgerTransfer>("The account is not active.")
                {
                    Code = "account_not_active"
                };
            }

            if (string.IsNullOrWhiteSpace(source.Currency) ||
                !string.Equals(source.Currency.Trim(), target.Currency?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync(cancellationToken);

                return new Fail<LedgerTransfer>("Transfers require accounts with the same currency.")
                {
                    Code = "currency_mismatch"
                };
            }

            if (request.ExpectedRecipientPhoneNumber is not null)
            {
                bool phoneStillBelongsToRecipient =
                    target.UserId is Guid recipientUserId &&
                    await _dbContext.Users
                        .AsNoTracking()
                        .AnyAsync(
                            user =>
                                user.Id == recipientUserId &&
                                user.PhoneNumber == request.ExpectedRecipientPhoneNumber &&
                                user.IsPhoneVerified &&
                                (user.PhoneVerificationMethod == PhoneVerificationMethod.SmsProvider ||
                                 (_bankingOptions.AllowLocalDemoPhoneRecipients &&
                                  user.PhoneVerificationMethod == PhoneVerificationMethod.LocalDemo)),
                            cancellationToken);

                if (!phoneStillBelongsToRecipient)
                {
                    await transaction.RollbackAsync(cancellationToken);

                    return new Fail<LedgerTransfer>("The verified phone recipient changed. Find the recipient again.")
                    {
                        Code = "recipient_phone_changed"
                    };
                }
            }

            if (request.Kind == LedgerTransferKind.UserTransfer)
            {
                DateTimeOffset fromUtcInclusive = new(
                    _clock.Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                DateTimeOffset toUtcExclusive = fromUtcInclusive.AddDays(1);
                Money alreadyTransferred = await _ledger.GetCompletedUserTransferTotalAsync(
                    request.RequestedByUserId,
                    fromUtcInclusive,
                    toUtcExclusive,
                    cancellationToken);
                Result<Money> dailyLimit = BankingMath.ValidateDailyLimit(
                    alreadyTransferred,
                    request.Amount,
                    _bankingOptions.Limits);

                if (dailyLimit.IsFailure())
                {
                    await transaction.RollbackAsync(cancellationToken);

                    return new Fail<LedgerTransfer>(dailyLimit.Error)
                    {
                        Code = ((Fail<Money>)dailyLimit).Code
                    };
                }
            }

            if (source.Balance < request.Amount)
            {
                await transaction.RollbackAsync(cancellationToken);

                return new Fail<LedgerTransfer>("There is not enough money on the account.")
                {
                    Code = "insufficient_funds"
                };
            }

            DateTimeOffset now = _clock.UtcNow;

            LedgerTransfer transfer = new(
                Guid.NewGuid(),
                request.IdempotencyKey,
                request.Kind,
                LedgerTransferStatus.Completed,
                source.Id,
                target.Id,
                request.RequestedByUserId,
                request.Amount,
                source.Currency,
                request.Description,
                null,
                now,
                now);

            Money sourceBalanceAfter = source.Balance - request.Amount;
            Money targetBalanceAfter = target.Balance + request.Amount;

            await _dbContext.LedgerTransfers.AddAsync(transfer, cancellationToken);

            await _dbContext.LedgerEntries.AddAsync(
                new LedgerEntry(
                    Guid.NewGuid(),
                    transfer.Id,
                    source.Id,
                    LedgerEntryDirection.Debit,
                    request.Amount,
                    sourceBalanceAfter,
                    now),
                cancellationToken);

            await _dbContext.LedgerEntries.AddAsync(
                new LedgerEntry(
                    Guid.NewGuid(),
                    transfer.Id,
                    target.Id,
                    LedgerEntryDirection.Credit,
                    request.Amount,
                    targetBalanceAfter,
                    now),
                cancellationToken);

            _dbContext.Entry(source).CurrentValues.SetValues(
                source with { Balance = sourceBalanceAfter, Version = source.Version + 1 });

            _dbContext.Entry(target).CurrentValues.SetValues(
                target with { Balance = targetBalanceAfter, Version = target.Version + 1 });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Ledger transfer {TransferId} of {Amount} committed from {SourceId} to {TargetId}.",
                transfer.Id,
                transfer.Amount.Cents,
                source.Id,
                target.Id);

            return new Ok<LedgerTransfer>(transfer);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(exception, "A ledger transfer was rejected because of a concurrent change.");

            return new Fail<LedgerTransfer>("The account was changed at the same time. Please try again.")
            {
                Code = "concurrency_conflict"
            };
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "A ledger transfer could not be committed.");

            return new Fail<LedgerTransfer>("The transfer could not be saved. Please try again.")
            {
                Code = "commit_failed"
            };
        }
    }
}
