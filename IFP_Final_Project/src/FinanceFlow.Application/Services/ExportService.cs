using System.Globalization;
using System.Text.Json;

using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Export;
using FinanceFlow.Application.Mapping;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Enums;

namespace FinanceFlow.Application.Services;

/// <summary>
/// Exports the financial data of a user as CSV or JSON.
/// The export is produced by pure functions, so the same data always yields the same file.
/// </summary>
public sealed class ExportService
{
    private readonly ITransactionRepository _transactions;
    private readonly ICategoryRepository _categories;
    private readonly IBudgetRepository _budgets;
    private readonly IClock _clock;

    /// <summary>Creates the export service.</summary>
    public ExportService(
        ITransactionRepository transactions,
        ICategoryRepository categories,
        IBudgetRepository budgets,
        IClock clock)
    {
        _transactions = transactions;
        _categories = categories;
        _budgets = budgets;
        _clock = clock;
    }

    /// <summary>Exports the transactions of a user as a CSV document.</summary>
    public async Task<string> TransactionsToCsvAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        (IReadOnlyList<Transaction> transactions, IReadOnlyList<Category> categories) =
            await LoadAsync(userId, cancellationToken);

        Dictionary<Guid, Category> categoryById = categories.ToDictionary(category => category.Id);

        List<IReadOnlyList<string>> rows = transactions
            .OrderByDescending(transaction => transaction.OccurredOn)
            .Select(transaction =>
            {
                Category? category = categoryById.GetValueOrDefault(transaction.CategoryId);

                return (IReadOnlyList<string>)new[]
                {
                    transaction.OccurredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    transaction.Kind == TransactionKind.Income ? "income" : "expense",
                    category?.Name ?? "Unknown",
                    transaction.Amount.ToDecimal().ToString("0.00", CultureInfo.InvariantCulture),
                    transaction.Note ?? string.Empty
                };
            })
            .ToList();

        return CsvWriter.Write(
            ["Date", "Type", "Category", "Amount", "Note"],
            rows);
    }

    /// <summary>Exports the transactions of a user as a JSON document.</summary>
    public async Task<string> TransactionsToJsonAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        (IReadOnlyList<Transaction> transactions, IReadOnlyList<Category> categories) =
            await LoadAsync(userId, cancellationToken);

        Dictionary<Guid, Category> categoryById = categories.ToDictionary(category => category.Id);

        List<ExportTransaction> rows = transactions
            .OrderByDescending(transaction => transaction.OccurredOn)
            .Select(transaction =>
            {
                Category? category = categoryById.GetValueOrDefault(transaction.CategoryId);

                return new ExportTransaction(
                    transaction.OccurredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    transaction.Kind == TransactionKind.Income ? "income" : "expense",
                    category?.Name ?? "Unknown",
                    transaction.Amount.ToDecimal(),
                    transaction.Note);
            })
            .ToList();

        return JsonSerializer.Serialize(
            rows,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
    }

    /// <summary>Exports the budgets of one month as a CSV document.</summary>
    public async Task<string> BudgetsToCsvAsync(
        Guid userId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Budget> budgets = await _budgets.GetAllAsync(userId, year, month, cancellationToken);
        IReadOnlyList<Category> categories = await _categories.GetAllAsync(userId, true, cancellationToken);

        Dictionary<Guid, Category> categoryById = categories.ToDictionary(category => category.Id);

        List<IReadOnlyList<string>> rows = budgets
            .Select(budget =>
            {
                Category? category = categoryById.GetValueOrDefault(budget.CategoryId);

                return (IReadOnlyList<string>)new[]
                {
                    year.ToString(CultureInfo.InvariantCulture),
                    month.ToString(CultureInfo.InvariantCulture),
                    category?.Name ?? "Unknown",
                    budget.Limit.ToDecimal().ToString("0.00", CultureInfo.InvariantCulture)
                };
            })
            .ToList();

        return CsvWriter.Write(["Year", "Month", "Category", "Limit"], rows);
    }

    /// <summary>Builds the file name of an export.</summary>
    public string BuildFileName(string kind, string extension)
    {
        DateOnly today = _clock.Today;
        return $"financeflow-{kind}-{today:yyyy-MM-dd}.{extension}";
    }

    private async Task<(IReadOnlyList<Transaction> Transactions, IReadOnlyList<Category> Categories)> LoadAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Transaction> transactions = await _transactions.GetAllAsync(userId, cancellationToken);
        IReadOnlyList<Category> categories = await _categories.GetAllAsync(userId, true, cancellationToken);

        return (transactions, categories);
    }
}

/// <summary>The shape of one exported transaction.</summary>
/// <param name="Date">The day of the transaction in the ISO format.</param>
/// <param name="Type">Either <c>income</c> or <c>expense</c>.</param>
/// <param name="Category">The name of the category.</param>
/// <param name="Amount">The amount in the main currency unit.</param>
/// <param name="Note">The optional note of the transaction.</param>
public sealed record ExportTransaction(string Date, string Type, string Category, decimal Amount, string? Note);
