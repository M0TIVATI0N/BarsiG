using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace FinanceFlow.Infrastructure.Persistence.Repositories;

/// <summary>Stores bank accounts in the database.</summary>
public sealed class BankAccountRepository : IBankAccountRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the bank account repository.</summary>
    public BankAccountRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<BankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.BankAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(account => account.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<BankAccount?> GetOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.BankAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(account => account.Id == id && account.UserId == userId, cancellationToken);

    /// <inheritdoc />
    public Task<BankAccount?> GetPrimaryAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.BankAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                account => account.UserId == userId && account.Type == BankAccountType.Primary,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BankAccount>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.BankAccounts
            .AsNoTracking()
            .Where(account => account.UserId == userId)
            .OrderBy(account => account.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BankAccount>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.BankAccounts
            .AsNoTracking()
            .OrderBy(account => account.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BankAccount>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await _dbContext.BankAccounts
            .AsNoTracking()
            .Where(account => ids.Contains(account.Id))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<BankAccount?> GetReserveAsync(CancellationToken cancellationToken = default) =>
        _dbContext.BankAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(account => account.Type == BankAccountType.Reserve, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(BankAccount account, CancellationToken cancellationToken = default) =>
        await _dbContext.BankAccounts.AddAsync(account, cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(BankAccount account, CancellationToken cancellationToken = default)
    {
        BankAccount? existing = await _dbContext.BankAccounts
            .FirstOrDefaultAsync(candidate => candidate.Id == account.Id, cancellationToken);

        if (existing is null)
        {
            return;
        }

        _dbContext.Entry(existing).CurrentValues.SetValues(account);
    }
}

/// <summary>Stores and reads the double entry ledger.</summary>
public sealed class LedgerRepository : ILedgerRepository
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the ledger repository.</summary>
    public LedgerRepository(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<LedgerTransfer?> GetTransferAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.LedgerTransfers
            .AsNoTracking()
            .FirstOrDefaultAsync(transfer => transfer.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<LedgerTransfer?> FindByIdempotencyKeyAsync(
        Guid requestedByUserId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        _dbContext.LedgerTransfers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                transfer => transfer.RequestedByUserId == requestedByUserId &&
                           transfer.IdempotencyKey == idempotencyKey,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerTransfer>> GetTransfersOfUserAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken = default) =>
        await _dbContext.LedgerTransfers
            .AsNoTracking()
            .Where(transfer => transfer.RequestedByUserId == userId)
            .OrderByDescending(transfer => transfer.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Money> GetCompletedUserTransferTotalAsync(
        Guid userId,
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcExclusive,
        CancellationToken cancellationToken = default)
    {
        if (toUtcExclusive <= fromUtcInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toUtcExclusive),
                "The end of the transfer range must be after its start.");
        }

        List<Money> amounts = await _dbContext.LedgerTransfers
            .AsNoTracking()
            .Where(transfer =>
                transfer.RequestedByUserId == userId &&
                transfer.Kind == LedgerTransferKind.UserTransfer &&
                transfer.Status == LedgerTransferStatus.Completed &&
                transfer.CreatedAtUtc >= fromUtcInclusive &&
                transfer.CreatedAtUtc < toUtcExclusive)
            .Select(transfer => transfer.Amount)
            .ToListAsync(cancellationToken);

        return new Money(amounts.Sum(amount => amount.Cents));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerEntry>> GetEntriesOfAccountAsync(
        Guid accountId,
        int take,
        CancellationToken cancellationToken = default) =>
        await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.AccountId == accountId)
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Money> ComputeBalanceAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        List<LedgerEntry> entries = await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.AccountId == accountId)
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
        {
            return Money.Zero;
        }

        return new Money(entries.Sum(entry => BankingMath.SignedCents(entry.Direction, entry.Amount)));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerAccountBalance>> ComputeAllBalancesAsync(
        CancellationToken cancellationToken = default)
    {
        List<LedgerEntry> entries = await _dbContext.LedgerEntries
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return entries
            .GroupBy(entry => entry.AccountId)
            .Select(group => new LedgerAccountBalance(
                group.Key,
                new Money(group.Sum(entry => BankingMath.SignedCents(entry.Direction, entry.Amount))),
                group.LongCount()))
            .ToList();
    }

    /// <inheritdoc />
    public Task<int> CountTransfersAsync(CancellationToken cancellationToken = default) =>
        _dbContext.LedgerTransfers.CountAsync(cancellationToken);
}

/// <summary>Finds the users that may receive a transfer.</summary>
public sealed class UserDirectory : IUserDirectory
{
    private readonly FinanceFlowDbContext _dbContext;

    /// <summary>Creates the user directory.</summary>
    public UserDirectory(FinanceFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecipientSuggestion>> SearchAsync(
        string query,
        Guid excludeUserId,
        int take,
        CancellationToken cancellationToken = default)
    {
        string term = query.Trim().ToLowerInvariant();

        IQueryable<User> users = _dbContext.Users.AsNoTracking();

        users = users.Where(user => !user.Email.EndsWith("@phone.invalid"));

        if (term.Length > 0)
        {
            users = users.Where(user =>
                user.DisplayName.ToLower().Contains(term) ||
                user.Email.Contains(term));
        }

        List<User> matches = await users
            .Where(user => user.Id != excludeUserId)
            .OrderBy(user => user.DisplayName)
            .Take(take)
            .ToListAsync(cancellationToken);

        return matches.Select(ToSuggestion).ToList();
    }

    /// <inheritdoc />
    public async Task<RecipientSuggestion?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        string normalized = email.Trim().ToLowerInvariant();

        User? user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Email == normalized && !candidate.Email.EndsWith("@phone.invalid"),
                cancellationToken);

        return user is null ? null : ToSuggestion(user);
    }

    /// <inheritdoc />
    public async Task<RecipientSuggestion?> FindByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        User? user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        return user is null ? null : ToSuggestion(user);
    }

    /// <inheritdoc />
    public async Task<RecipientSuggestion?> FindByVerifiedPhoneNumberAsync(
        string canonicalPhoneNumber,
        bool allowLocalDemo,
        CancellationToken cancellationToken = default)
    {
        string phone = canonicalPhoneNumber.Trim();

        User? user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate =>
                    candidate.PhoneNumber == phone &&
                    candidate.IsPhoneVerified &&
                    (candidate.PhoneVerificationMethod == PhoneVerificationMethod.SmsProvider ||
                     (allowLocalDemo && candidate.PhoneVerificationMethod == PhoneVerificationMethod.LocalDemo)),
                cancellationToken);

        return user is null
            ? null
            : new RecipientSuggestion(user.Id, user.DisplayName, string.Empty);
    }

    /// <summary>
    /// Builds the suggestion shown to the user. The e-mail address is hidden in the middle,
    /// so the directory of the bank never leaks the whole address of a customer.
    /// </summary>
    private static RecipientSuggestion ToSuggestion(User user) =>
        new(
            user.Id,
            user.DisplayName,
            user.Email.EndsWith("@phone.invalid", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : Mask(user.Email));

    /// <summary>Hides the middle part of an e-mail address.</summary>
    public static string Mask(string email)
    {
        int at = email.IndexOf('@', StringComparison.Ordinal);

        if (at <= 1)
        {
            return email;
        }

        string local = email[..at];
        string domain = email[at..];
        string visible = local[..1];

        return $"{visible}{new string('*', Math.Max(local.Length - 1, 1))}{domain}";
    }
}
