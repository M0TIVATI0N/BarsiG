using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;

namespace FinanceFlow.UnitTests.Fakes;

/// <summary>An in memory repository of bank accounts.</summary>
public sealed class FakeBankAccountRepository : IBankAccountRepository
{
    private readonly List<BankAccount> _accounts = [];

    /// <summary>Adds an account to the repository.</summary>
    public void Seed(BankAccount account) => _accounts.Add(account);

    /// <inheritdoc />
    public Task<BankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accounts.FirstOrDefault(account => account.Id == id));

    /// <inheritdoc />
    public Task<BankAccount?> GetOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accounts.FirstOrDefault(account => account.Id == id && account.UserId == userId));

    /// <inheritdoc />
    public Task<BankAccount?> GetPrimaryAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accounts.FirstOrDefault(account =>
            account.UserId == userId && account.Type == BankAccountType.Primary));

    /// <inheritdoc />
    public Task<IReadOnlyList<BankAccount>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BankAccount>>(_accounts
            .Where(account => account.UserId == userId)
            .OrderBy(account => account.CreatedAtUtc)
            .ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<BankAccount>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BankAccount>>(_accounts.ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<BankAccount>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BankAccount>>(_accounts
            .Where(account => ids.Contains(account.Id))
            .ToList());

    /// <inheritdoc />
    public Task<BankAccount?> GetReserveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_accounts.FirstOrDefault(account => account.Type == BankAccountType.Reserve));

    /// <inheritdoc />
    public Task AddAsync(BankAccount account, CancellationToken cancellationToken = default)
    {
        _accounts.Add(account);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(BankAccount account, CancellationToken cancellationToken = default)
    {
        int index = _accounts.FindIndex(candidate => candidate.Id == account.Id);

        if (index >= 0)
        {
            _accounts[index] = account;
        }

        return Task.CompletedTask;
    }

    /// <summary>Sets the balance of an account without touching the ledger.</summary>
    public void SetBalance(Guid accountId, Money balance)
    {
        int index = _accounts.FindIndex(account => account.Id == accountId);

        if (index >= 0)
        {
            _accounts[index] = _accounts[index] with { Balance = balance };
        }
    }
}

/// <summary>An in memory repository of the double entry ledger.</summary>
public sealed class FakeLedgerRepository : ILedgerRepository
{
    private readonly List<LedgerTransfer> _transfers = [];
    private readonly List<LedgerEntry> _entries = [];

    /// <summary>Adds a transfer with its two entries to the repository.</summary>
    public void Seed(LedgerTransfer transfer, LedgerEntry debit, LedgerEntry credit)
    {
        _transfers.Add(transfer);
        _entries.Add(debit);
        _entries.Add(credit);
    }

    /// <inheritdoc />
    public Task<LedgerTransfer?> GetTransferAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_transfers.FirstOrDefault(transfer => transfer.Id == id));

    /// <inheritdoc />
    public Task<LedgerTransfer?> FindByIdempotencyKeyAsync(
        Guid requestedByUserId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_transfers.FirstOrDefault(transfer =>
            transfer.RequestedByUserId == requestedByUserId &&
            transfer.IdempotencyKey == idempotencyKey));

    /// <inheritdoc />
    public Task<IReadOnlyList<LedgerTransfer>> GetTransfersOfUserAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerTransfer>>(_transfers
            .Where(transfer => transfer.RequestedByUserId == userId)
            .OrderByDescending(transfer => transfer.CreatedAtUtc)
            .Take(take)
            .ToList());

    /// <inheritdoc />
    public Task<Money> GetCompletedUserTransferTotalAsync(
        Guid userId,
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcExclusive,
        CancellationToken cancellationToken = default)
    {
        long total = _transfers
            .Where(transfer => transfer.RequestedByUserId == userId)
            .Where(transfer => transfer.Kind == LedgerTransferKind.UserTransfer)
            .Where(transfer => transfer.Status == LedgerTransferStatus.Completed)
            .Where(transfer => transfer.CreatedAtUtc >= fromUtcInclusive)
            .Where(transfer => transfer.CreatedAtUtc < toUtcExclusive)
            .Sum(transfer => transfer.Amount.Cents);

        return Task.FromResult(new Money(total));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LedgerEntry>> GetEntriesOfAccountAsync(
        Guid accountId,
        int take,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LedgerEntry>>(_entries
            .Where(entry => entry.AccountId == accountId)
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .Take(take)
            .ToList());

    /// <inheritdoc />
    public Task<Money> ComputeBalanceAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        long total = _entries
            .Where(entry => entry.AccountId == accountId)
            .Sum(entry => BankingMath.SignedCents(entry.Direction, entry.Amount));

        return Task.FromResult(new Money(total));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LedgerAccountBalance>> ComputeAllBalancesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LedgerAccountBalance> balances = _entries
            .GroupBy(entry => entry.AccountId)
            .Select(group => new LedgerAccountBalance(
                group.Key,
                new Money(group.Sum(entry => BankingMath.SignedCents(entry.Direction, entry.Amount))),
                group.LongCount()))
            .ToList();

        return Task.FromResult<IReadOnlyList<LedgerAccountBalance>>(balances);
    }

    /// <inheritdoc />
    public Task<int> CountTransfersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_transfers.Count);
}

/// <summary>
/// A ledger executor that works in memory. It repeats the rules of the real one:
/// the money is never created, the balance is checked before the movement and a repeated
/// idempotency key never moves the money twice.
/// </summary>
public sealed class FakeLedgerExecutor : ILedgerExecutor
{
    private readonly FakeBankAccountRepository _accounts;
    private readonly FakeLedgerRepository _ledger;
    private readonly IClock _clock;

    /// <summary>The number of executed movements.</summary>
    public int ExecuteCount { get; private set; }

    /// <summary>Creates the fake executor.</summary>
    public FakeLedgerExecutor(FakeBankAccountRepository accounts, FakeLedgerRepository ledger, IClock clock)
    {
        _accounts = accounts;
        _ledger = ledger;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<Result<LedgerTransfer>> ExecuteAsync(
        LedgerExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        LedgerTransfer? replay = _ledger.GetTransfersOfUserAsync(request.RequestedByUserId, 1000).GetAwaiter().GetResult()
            .FirstOrDefault(transfer => transfer.IdempotencyKey == request.IdempotencyKey);

        if (replay is not null)
        {
            return Task.FromResult<Result<LedgerTransfer>>(new Ok<LedgerTransfer>(replay));
        }

        if (request.Amount.IsNegative || request.Amount.IsZero)
        {
            return Task.FromResult<Result<LedgerTransfer>>(new Fail<LedgerTransfer>("The amount must be greater than zero.")
            {
                Code = "invalid_amount"
            });
        }

        if (request.FromAccountId == request.ToAccountId)
        {
            return Task.FromResult<Result<LedgerTransfer>>(
                new Fail<LedgerTransfer>("The source and the target account must be different.")
                {
                    Code = "same_account"
                });
        }

        BankAccount? source = _accounts.GetByIdAsync(request.FromAccountId).GetAwaiter().GetResult();
        BankAccount? target = _accounts.GetByIdAsync(request.ToAccountId).GetAwaiter().GetResult();

        if (source is null || target is null)
        {
            return Task.FromResult<Result<LedgerTransfer>>(new Fail<LedgerTransfer>("The account does not exist.")
            {
                Code = "account_not_found"
            });
        }

        if (source.Balance < request.Amount)
        {
            return Task.FromResult<Result<LedgerTransfer>>(
                new Fail<LedgerTransfer>("There is not enough money on the account.")
                {
                    Code = "insufficient_funds"
                });
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

        _ledger.Seed(
            transfer,
            new LedgerEntry(
                Guid.NewGuid(),
                transfer.Id,
                source.Id,
                LedgerEntryDirection.Debit,
                request.Amount,
                source.Balance - request.Amount,
                now),
            new LedgerEntry(
                Guid.NewGuid(),
                transfer.Id,
                target.Id,
                LedgerEntryDirection.Credit,
                request.Amount,
                target.Balance + request.Amount,
                now));

        _accounts.SetBalance(source.Id, source.Balance - request.Amount);
        _accounts.SetBalance(target.Id, target.Balance + request.Amount);

        ExecuteCount++;

        return Task.FromResult<Result<LedgerTransfer>>(new Ok<LedgerTransfer>(transfer));
    }
}

/// <summary>An in memory repository of the security profiles.</summary>
public sealed class FakeSecurityProfileRepository : ISecurityProfileRepository
{
    private readonly List<UserSecurityProfile> _profiles = [];

    /// <summary>Adds a profile to the repository.</summary>
    public void Seed(UserSecurityProfile profile) => _profiles.Add(profile);

    /// <inheritdoc />
    public Task<UserSecurityProfile?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_profiles.FirstOrDefault(profile => profile.UserId == userId));

    /// <inheritdoc />
    public Task AddAsync(UserSecurityProfile profile, CancellationToken cancellationToken = default)
    {
        _profiles.Add(profile);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(UserSecurityProfile profile, CancellationToken cancellationToken = default)
    {
        int index = _profiles.FindIndex(candidate => candidate.Id == profile.Id);

        if (index >= 0)
        {
            _profiles[index] = profile;
        }

        return Task.CompletedTask;
    }
}

/// <summary>An in memory directory of the possible recipients.</summary>
public sealed class FakeUserDirectory : IUserDirectory
{
    private readonly List<RecipientSuggestion> _recipients = [];
    private readonly Dictionary<string, (RecipientSuggestion Recipient, bool IsLocalDemo)> _verifiedPhones = [];

    /// <summary>Adds a recipient to the directory.</summary>
    public void Seed(RecipientSuggestion recipient) => _recipients.Add(recipient);

    /// <summary>Adds a verified phone identity to the in-memory directory.</summary>
    public void SeedVerifiedPhone(
        string phoneNumber,
        RecipientSuggestion recipient,
        bool isLocalDemo = true) =>
        _verifiedPhones[phoneNumber] = (recipient, isLocalDemo);

    /// <inheritdoc />
    public Task<IReadOnlyList<RecipientSuggestion>> SearchAsync(
        string query,
        Guid excludeUserId,
        int take,
        CancellationToken cancellationToken = default)
    {
        string term = query.Trim();

        return Task.FromResult<IReadOnlyList<RecipientSuggestion>>(_recipients
            .Where(recipient => recipient.UserId != excludeUserId)
            .Where(recipient => term.Length == 0 ||
                                recipient.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(take)
            .ToList());
    }

    /// <inheritdoc />
    public Task<RecipientSuggestion?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        string normalized = email.Trim().ToLowerInvariant();

        return Task.FromResult(_recipients.FirstOrDefault(recipient =>
            recipient.MaskedEmail.ToLowerInvariant() == normalized));
    }

    /// <inheritdoc />
    public Task<RecipientSuggestion?> FindByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_recipients.FirstOrDefault(recipient => recipient.UserId == userId));

    /// <inheritdoc />
    public Task<RecipientSuggestion?> FindByVerifiedPhoneNumberAsync(
        string canonicalPhoneNumber,
        bool allowLocalDemo,
        CancellationToken cancellationToken = default)
    {
        if (!_verifiedPhones.TryGetValue(canonicalPhoneNumber, out var entry) ||
            (entry.IsLocalDemo && !allowLocalDemo))
        {
            return Task.FromResult<RecipientSuggestion?>(null);
        }

        return Task.FromResult<RecipientSuggestion?>(entry.Recipient);
    }
}
