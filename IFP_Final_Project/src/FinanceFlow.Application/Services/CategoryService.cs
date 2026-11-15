using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Dtos;
using FinanceFlow.Application.Mapping;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.Application.Services;

/// <summary>Manages the income and expense categories of a user.</summary>
public sealed class CategoryService
{
    /// <summary>The maximum length of a category name.</summary>
    public const int NameMaxLength = 30;

    private readonly ICategoryRepository _categories;
    private readonly ITransactionRepository _transactions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IDataChangeNotifier _notifier;

    /// <summary>Creates the category service.</summary>
    public CategoryService(
        ICategoryRepository categories,
        ITransactionRepository transactions,
        IUnitOfWork unitOfWork,
        IClock clock,
        IDataChangeNotifier notifier)
    {
        _categories = categories;
        _transactions = transactions;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _notifier = notifier;
    }

    /// <summary>Returns all categories of the user together with the usage of the current month.</summary>
    public async Task<IReadOnlyList<CategoryItem>> GetAllAsync(
        Guid userId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Category> categories = await _categories.GetAllAsync(userId, includeArchived, cancellationToken);
        IReadOnlyList<Transaction> transactions = await _transactions.GetAllAsync(userId, cancellationToken);

        DateOnly today = _clock.Today;
        DateOnly monthStart = new(today.Year, today.Month, 1);

        return categories
            .Select(category =>
            {
                List<Transaction> categoryTransactions = transactions
                    .Where(transaction => transaction.CategoryId == category.Id)
                    .ToList();

                Money spent = new(categoryTransactions.Sum(transaction => transaction.Amount.Cents));

                return EntityMapper.ToItem(category, spent, categoryTransactions.Count);
            })
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Creates a new category.</summary>
    public async Task<Result<CategoryItem>> CreateAsync(
        Guid userId,
        string name,
        TransactionKind kind,
        string colorHex,
        string icon,
        CancellationToken cancellationToken = default)
    {
        Result<string> validName = Validation.Required(name, "Name", "The category name is required.");
        Result<string> validNameLength = Validation.MaxLength(name, NameMaxLength, "Name", $"The name must not be longer than {NameMaxLength} characters.");
        Result<string> validColor = Validation.HexColor(colorHex, "ColorHex");

        ValidationBuilder builder = new ValidationBuilder()
            .Add(validName)
            .Add(validNameLength)
            .Add(validColor);

        if (!builder.IsValid)
        {
            return builder.Failure<CategoryItem>();
        }

        string normalizedName = validName.ValueOr(string.Empty);
        IReadOnlyList<Category> existing = await _categories.GetAllAsync(userId, true, cancellationToken);

        if (existing.Any(category =>
                string.Equals(category.Name, normalizedName, StringComparison.CurrentCultureIgnoreCase) &&
                category.Kind == kind))
        {
            return new Fail<CategoryItem>("A category with this name already exists.")
            {
                Code = "duplicate",
                Details = [new ValidationError("Name", "A category with this name already exists.")]
            };
        }

        Category category = new(
            Guid.NewGuid(),
            userId,
            normalizedName,
            kind,
            validColor.ValueOr("#4F46E5"),
            string.IsNullOrWhiteSpace(icon) ? "tag" : icon.Trim(),
            false,
            _clock.UtcNow);

        await _categories.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyAsync(userId, "categories", "created", cancellationToken);

        return new Ok<CategoryItem>(EntityMapper.ToItem(category, Money.Zero, 0));
    }

    /// <summary>Updates an existing category.</summary>
    public async Task<Result<CategoryItem>> UpdateAsync(
        Guid userId,
        Guid categoryId,
        string name,
        TransactionKind kind,
        string colorHex,
        string icon,
        bool isArchived,
        CancellationToken cancellationToken = default)
    {
        Category? category = await _categories.GetByIdAsync(categoryId, userId, cancellationToken);
        if (category is null)
        {
            return new Fail<CategoryItem>("The category was not found.") { Code = "not_found" };
        }

        Result<string> validName = Validation.Required(name, "Name", "The category name is required.");
        Result<string> validColor = Validation.HexColor(colorHex, "ColorHex");

        ValidationBuilder builder = new ValidationBuilder()
            .Add(validName)
            .Add(validColor);

        if (!builder.IsValid)
        {
            return builder.Failure<CategoryItem>();
        }

        Category updated = category with
        {
            Name = validName.ValueOr(category.Name),
            Kind = kind,
            ColorHex = validColor.ValueOr(category.ColorHex),
            Icon = string.IsNullOrWhiteSpace(icon) ? category.Icon : icon.Trim(),
            IsArchived = isArchived
        };

        await _categories.UpdateAsync(updated, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyAsync(userId, "categories", "updated", cancellationToken);

        IReadOnlyList<Transaction> transactions = await _transactions.GetAllAsync(userId, cancellationToken);
        int count = transactions.Count(transaction => transaction.CategoryId == categoryId);

        return new Ok<CategoryItem>(EntityMapper.ToItem(updated, Money.Zero, count));
    }

    /// <summary>Removes a category that is not used by any transaction.</summary>
    public async Task<Result> DeleteAsync(
        Guid userId,
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        Category? category = await _categories.GetByIdAsync(categoryId, userId, cancellationToken);
        if (category is null)
        {
            return new Result.Failure("The category was not found.") { Code = "not_found" };
        }

        IReadOnlyList<Transaction> transactions = await _transactions.GetAllAsync(userId, cancellationToken);
        bool used = transactions.Any(transaction => transaction.CategoryId == categoryId);

        if (used)
        {
            return new Result.Failure("The category still has transactions. Archive it instead.")
            {
                Code = "category_in_use"
            };
        }

        await _categories.DeleteAsync(categoryId, userId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyAsync(userId, "categories", "deleted", cancellationToken);

        return Result.Success.Instance;
    }
}
