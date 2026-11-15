using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinanceFlow.Infrastructure.Seeding;

/// <summary>
/// Fills an empty database with a demonstration account and six months of realistic data.
/// It makes the first run of the application immediately useful and gives the tests
/// a stable data set to work with.
/// </summary>
public static class DemoDataSeeder
{
    /// <summary>The e-mail address of the demonstration account.</summary>
    public const string DemoEmail = "demo@financeflow.app";

    /// <summary>The number of months of history that are generated.</summary>
    public const int HistoryMonths = 6;

    /// <summary>Seeds the database when it does not contain the demonstration account yet.</summary>
    public static async Task SeedAsync(
        IServiceProvider services,
        string? configuredPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using IServiceScope scope = services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;

        IUserRepository users = provider.GetRequiredService<IUserRepository>();
        IPasswordHasher hasher = provider.GetRequiredService<IPasswordHasher>();
        IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoDataSeeder));

        if (await users.ExistsAsync(DemoEmail, cancellationToken))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(configuredPassword) || configuredPassword.Length < 12)
        {
            logger.LogWarning(
                "The demonstration account was not created because BARSIG_DEMO_PASSWORD is missing or too short.");
            return;
        }

        (string hash, string salt) = hasher.Hash(configuredPassword);

        User user = new(
            Guid.NewGuid(),
            DemoEmail,
            "Demo User",
            hash,
            salt,
            DateTimeOffset.UtcNow);

        await users.AddAsync(user, cancellationToken);

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly monthStart = new(today.Year, today.Month, 1);

        List<Category> categories = CreateCategories(user.Id);
        foreach (Category category in categories)
        {
            provider.GetRequiredService<ICategoryRepository>();
            await provider.GetRequiredService<ICategoryRepository>().AddAsync(category, cancellationToken);
        }

        ITransactionRepository transactions = provider.GetRequiredService<ITransactionRepository>();
        List<Transaction> generated = GenerateTransactions(user.Id, categories, monthStart, today);
        foreach (Transaction transaction in generated)
        {
            await transactions.AddAsync(transaction, cancellationToken);
        }

        IBudgetRepository budgets = provider.GetRequiredService<IBudgetRepository>();
        foreach (Budget budget in CreateBudgets(user.Id, categories, today))
        {
            await budgets.AddAsync(budget, cancellationToken);
        }

        ISavingsGoalRepository goals = provider.GetRequiredService<ISavingsGoalRepository>();
        foreach (SavingsGoal goal in CreateGoals(user.Id, today))
        {
            await goals.AddAsync(goal, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("The demonstration account {Email} was created.", DemoEmail);
    }

    private static List<Category> CreateCategories(Guid userId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        (string Name, TransactionKind Kind, string Color, string Icon)[] definitions =
        [
            ("Salary", TransactionKind.Income, "#10B981", "briefcase"),
            ("Freelance", TransactionKind.Income, "#34D399", "code"),
            ("Groceries", TransactionKind.Expense, "#F59E0B", "cart"),
            ("Rent", TransactionKind.Expense, "#6366F1", "home"),
            ("Transport", TransactionKind.Expense, "#0EA5E9", "bus"),
            ("Utilities", TransactionKind.Expense, "#8B5CF6", "bolt"),
            ("Health", TransactionKind.Expense, "#EF4444", "heart"),
            ("Entertainment", TransactionKind.Expense, "#EC4899", "film"),
            ("Shopping", TransactionKind.Expense, "#F43F5E", "bag"),
            ("Education", TransactionKind.Expense, "#3B82F6", "book")
        ];

        return definitions
            .Select(definition => new Category(
                Guid.NewGuid(),
                userId,
                definition.Name,
                definition.Kind,
                definition.Color,
                definition.Icon,
                false,
                now))
            .ToList();
    }

    private static List<Transaction> GenerateTransactions(
        Guid userId,
        List<Category> categories,
        DateOnly monthStart,
        DateOnly today)
    {
        Random random = new(20241101);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Dictionary<string, Category> byName = categories.ToDictionary(category => category.Name);
        List<Transaction> transactions = [];

        for (int monthOffset = HistoryMonths - 1; monthOffset >= 0; monthOffset--)
        {
            DateOnly month = monthStart.AddMonths(-monthOffset);
            int daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);

            void Add(string categoryName, TransactionKind kind, int day, decimal amount, string? note)
            {
                DateOnly date = new(month.Year, month.Month, Math.Min(Math.Max(day, 1), daysInMonth));

                if (date > today)
                {
                    return;
                }

                transactions.Add(new Transaction(
                    Guid.NewGuid(),
                    userId,
                    byName[categoryName].Id,
                    Money.FromDecimal(amount),
                    kind,
                    date,
                    note,
                    false,
                    now));
            }

            Add("Salary", TransactionKind.Income, 5, 320_000m, "Monthly salary");
            Add("Rent", TransactionKind.Expense, 1, 120_000m, "Apartment rent");

            if (random.NextDouble() < 0.7)
            {
                Add("Freelance", TransactionKind.Income, random.Next(8, 26), random.Next(35_000, 95_000), "Order for a client");
            }

            Add("Utilities", TransactionKind.Expense, 8, random.Next(16_000, 28_000), "Electricity and internet");

            for (int index = 0; index < random.Next(4, 8); index++)
            {
                Add("Groceries", TransactionKind.Expense, random.Next(1, daysInMonth), random.Next(3_500, 17_000), "Supermarket");
            }

            for (int index = 0; index < random.Next(2, 6); index++)
            {
                Add("Transport", TransactionKind.Expense, random.Next(1, daysInMonth), random.Next(1_500, 9_500), "Transport card");
            }

            if (random.NextDouble() < 0.35)
            {
                Add("Health", TransactionKind.Expense, random.Next(3, daysInMonth), random.Next(5_000, 42_000), "Pharmacy or doctor");
            }

            for (int index = 0; index < random.Next(2, 5); index++)
            {
                Add("Entertainment", TransactionKind.Expense, random.Next(1, daysInMonth), random.Next(2_500, 16_000), "Cinema or cafe");
            }

            for (int index = 0; index < random.Next(1, 4); index++)
            {
                Add("Shopping", TransactionKind.Expense, random.Next(1, daysInMonth), random.Next(9_000, 48_000), "Clothes and shoes");
            }

            if (random.NextDouble() < 0.4)
            {
                Add("Education", TransactionKind.Expense, random.Next(5, daysInMonth), random.Next(12_000, 40_000), "Online course");
            }
        }

        return transactions;
    }

    private static List<Budget> CreateBudgets(Guid userId, List<Category> categories, DateOnly today)
    {
        Dictionary<string, Category> byName = categories.ToDictionary(category => category.Name);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        (string Category, decimal Limit)[] definitions =
        [
            ("Groceries", 60_000m),
            ("Transport", 25_000m),
            ("Entertainment", 30_000m),
            ("Shopping", 50_000m)
        ];

        return definitions
            .Select(definition => new Budget(
                Guid.NewGuid(),
                userId,
                byName[definition.Category].Id,
                today.Year,
                today.Month,
                Money.FromDecimal(definition.Limit),
                now))
            .ToList();
    }

    private static List<SavingsGoal> CreateGoals(Guid userId, DateOnly today)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return
        [
            new SavingsGoal(
                Guid.NewGuid(),
                userId,
                "Emergency fund",
                Money.FromDecimal(500_000m),
                Money.FromDecimal(185_000m),
                today.AddMonths(10),
                GoalStatus.Active,
                "Six months of living expenses",
                now),
            new SavingsGoal(
                Guid.NewGuid(),
                userId,
                "New laptop",
                Money.FromDecimal(350_000m),
                Money.FromDecimal(318_500m),
                today.AddMonths(2),
                GoalStatus.Active,
                "A laptop for the university projects",
                now)
        ];
    }
}
