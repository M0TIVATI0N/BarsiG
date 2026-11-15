using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Dtos;
using FinanceFlow.Application.Mapping;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Models;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.Application.Services;

/// <summary>
/// Manages the financial transactions of a user.
/// The create and update operations are built as a pipeline of small steps:
/// <c>Draft -&gt; Validated -&gt; Stored</c>. Each step returns a result, so a broken
/// input can never reach the database and the compiler shows the stages in the signature.
/// </summary>
public sealed class TransactionService
{
    private readonly ITransactionRepository _transactions;
    private readonly ICategoryRepository _categories;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IDataChangeNotifier _notifier;

    /// <summary>Creates the transaction service.</summary>
    public TransactionService(
        ITransactionRepository transactions,
        ICategoryRepository categories,
        IUnitOfWork unitOfWork,
        IClock clock,
        IDataChangeNotifier notifier)
    {
        _transactions = transactions;
        _categories = categories;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _notifier = notifier;
    }

    /// <summary>
    /// Returns a filtered, sorted and paginated list of transactions.
    /// The filtering uses the hand written lazy operators of the domain layer.
    /// </summary>
    public async Task<PagedResult<TransactionItem>> GetListAsync(
        Guid userId,
        TransactionFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IReadOnlyList<Transaction> all = await _transactions.GetAllAsync(userId, cancellationToken);
        IReadOnlyList<Category> categories = await _categories.GetAllAsync(userId, true, cancellationToken);
        Dictionary<Guid, Category> categoryById = categories.ToDictionary(category => category.Id);

        IEnumerable<Transaction> query = all;

        if (filter.Kind is { } kind)
        {
            query = query.Filter(transaction => transaction.Kind == kind);
        }

        if (filter.CategoryId is { } categoryId)
        {
            query = query.Filter(transaction => transaction.CategoryId == categoryId);
        }

        if (filter.From is { } from)
        {
            query = query.Filter(transaction => transaction.OccurredOn >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Filter(transaction => transaction.OccurredOn <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string search = filter.Search.Trim();
            query = query.Filter(transaction =>
                (transaction.Note?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (categoryById.TryGetValue(transaction.CategoryId, out Category? category) &&
                 category.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)));
        }

        List<Transaction> ordered = query
            .OrderByDescending(transaction => transaction.OccurredOn)
            .ThenByDescending(transaction => transaction.CreatedAtUtc)
            .ToList();

        int pageSize = Math.Clamp(filter.PageSize, 1, 100);
        int page = Math.Max(filter.Page, 1);
        int totalCount = ordered.Count;

        List<TransactionItem> items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Transform(transaction => EntityMapper.ToItem(
                transaction,
                categoryById.GetValueOrDefault(transaction.CategoryId)))
            .ToList();

        return new PagedResult<TransactionItem>(items, totalCount, page, pageSize);
    }

    /// <summary>Returns one transaction of the user.</summary>
    public async Task<Result<TransactionItem>> GetByIdAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        Transaction? transaction = await _transactions.GetByIdAsync(transactionId, userId, cancellationToken);
        if (transaction is null)
        {
            return new Fail<TransactionItem>("The transaction was not found.") { Code = "not_found" };
        }

        Category? category = await _categories.GetByIdAsync(transaction.CategoryId, userId, cancellationToken);
        return new Ok<TransactionItem>(EntityMapper.ToItem(transaction, category));
    }

    /// <summary>
    /// Creates a transaction. The input is validated by pure functions first,
    /// then the category is checked, and only then the record is written.
    /// </summary>
    public async Task<Result<TransactionItem>> CreateAsync(
        Guid userId,
        TransactionDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        Result<ValidatedTransaction> validated = await TransactionValidator.Validate(draft)
            .BindAsync((ValidatedTransaction value) => EnsureCategoryAsync(userId, value, cancellationToken));

        return await validated
            .BindAsync((ValidatedTransaction value) => StoreAsync(userId, value, cancellationToken));
    }

    /// <summary>Updates an existing transaction with the same validation pipeline.</summary>
    public async Task<Result<TransactionItem>> UpdateAsync(
        Guid userId,
        Guid transactionId,
        TransactionDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        Transaction? existing = await _transactions.GetByIdAsync(transactionId, userId, cancellationToken);
        if (existing is null)
        {
            return new Fail<TransactionItem>("The transaction was not found.") { Code = "not_found" };
        }

        Result<ValidatedTransaction> validated = await TransactionValidator.Validate(draft)
            .BindAsync((ValidatedTransaction value) => EnsureCategoryAsync(userId, value, cancellationToken));

        return await validated
            .BindAsync((ValidatedTransaction value) => ReplaceAsync(userId, existing, value, cancellationToken));
    }

    /// <summary>Removes a transaction.</summary>
    public async Task<Result> DeleteAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        Transaction? existing = await _transactions.GetByIdAsync(transactionId, userId, cancellationToken);
        if (existing is null)
        {
            return new Result.Failure("The transaction was not found.") { Code = "not_found" };
        }

        await _transactions.DeleteAsync(transactionId, userId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyAsync(userId, "transactions", "deleted", cancellationToken);

        return Result.Success.Instance;
    }

    private async Task<Result<ValidatedTransaction>> EnsureCategoryAsync(
        Guid userId,
        ValidatedTransaction validated,
        CancellationToken cancellationToken)
    {
        Category? category = await _categories.GetByIdAsync(validated.CategoryId, userId, cancellationToken);

        if (category is null)
        {
            return new Fail<ValidatedTransaction>("The selected category does not exist.")
            {
                Code = "not_found",
                Details = [new ValidationError("CategoryId", "The selected category does not exist.")]
            };
        }

        if (category.Kind != validated.Kind)
        {
            return new Fail<ValidatedTransaction>("The transaction type does not match the category type.")
            {
                Code = "kind_mismatch",
                Details = [new ValidationError("Kind", "The transaction type does not match the category type.")]
            };
        }

        return new Ok<ValidatedTransaction>(validated);
    }

    private async Task<Result<TransactionItem>> StoreAsync(
        Guid userId,
        ValidatedTransaction validated,
        CancellationToken cancellationToken)
    {
        Transaction transaction = new(
            Guid.NewGuid(),
            userId,
            validated.CategoryId,
            validated.Amount,
            validated.Kind,
            validated.OccurredOn,
            validated.Note,
            false,
            _clock.UtcNow);

        await _transactions.AddAsync(transaction, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyAsync(userId, "transactions", "created", cancellationToken);

        Category? category = await _categories.GetByIdAsync(validated.CategoryId, userId, cancellationToken);
        return new Ok<TransactionItem>(EntityMapper.ToItem(transaction, category));
    }

    private async Task<Result<TransactionItem>> ReplaceAsync(
        Guid userId,
        Transaction existing,
        ValidatedTransaction validated,
        CancellationToken cancellationToken)
    {
        Transaction updated = existing with
        {
            CategoryId = validated.CategoryId,
            Amount = validated.Amount,
            Kind = validated.Kind,
            OccurredOn = validated.OccurredOn,
            Note = validated.Note
        };

        await _transactions.UpdateAsync(updated, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyAsync(userId, "transactions", "updated", cancellationToken);

        Category? category = await _categories.GetByIdAsync(validated.CategoryId, userId, cancellationToken);
        return new Ok<TransactionItem>(EntityMapper.ToItem(updated, category));
    }
}
