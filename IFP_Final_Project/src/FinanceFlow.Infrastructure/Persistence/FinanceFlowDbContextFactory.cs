using FinanceFlow.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinanceFlow.Infrastructure.Persistence;

/// <summary>
/// Creates the persistence context for the design time tools, for example <c>dotnet ef migrations</c>.
/// The tools must not start the whole web application, therefore the context is built by hand.
/// </summary>
public sealed class FinanceFlowDbContextFactory : IDesignTimeDbContextFactory<FinanceFlowDbContext>
{
    /// <inheritdoc />
    public FinanceFlowDbContext CreateDbContext(string[] args)
    {
        string connectionString = args.Length > 0
            ? args[0]
            : "Data Source=financeflow.db";

        DbContextOptionsBuilder<FinanceFlowDbContext> options = new();
        options.UseSqlite(connectionString);

        return new FinanceFlowDbContext(options.Options);
    }
}
