using FinanceFlow.Application;
using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Infrastructure;
using FinanceFlow.Infrastructure.Persistence;
using FinanceFlow.Infrastructure.Seeding;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceFlow.IntegrationTests;

/// <summary>
/// Proves the bootstrap policy of the administrator of the Demo Bank.
/// The rule is narrow on purpose: the seeder may only create a brand new administrator,
/// it never promotes an ordinary customer and it never resets a stored password.
/// </summary>
public sealed class AdminSeederTests : IDisposable
{
    private const string AdminEmail = "admin@barsig.test";
    private const string AdminPassword = "BarsiG_Seeder_Test_2026!";

    private readonly string _databasePath;

    /// <summary>Creates a database that lives only for the duration of one test.</summary>
    public AdminSeederTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"barsig-seed-{Guid.NewGuid():N}.db");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FinanceFlow"] = $"Data Source={_databasePath}"
            })
            .Build();

        ServiceCollection collection = new();

        collection.AddApplication();
        collection.AddInfrastructure(configuration);
        collection.AddSignalR();
        collection.AddLogging();

        Services = collection.BuildServiceProvider();

        using IServiceScope scope = Services.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<FinanceFlowDbContext>()
            .Database
            .MigrateAsync()
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>The services of the application.</summary>
    public ServiceProvider Services { get; }

    /// <summary>A fresh configuration that carries the credentials of the administrator.</summary>
    private static IConfiguration BuildConfiguration(string email, string password)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BARSIG_ADMIN_EMAIL"] = email,
                ["BARSIG_ADMIN_PASSWORD"] = password
            })
            .Build();
    }

    /// <summary>
    /// Runs the seeder and returns its outcome. The environment variables are set and
    /// restored explicitly, so the result never depends on the machine that runs the test.
    /// </summary>
    private async Task<AdminSeedOutcome> SeedAsync(string email = AdminEmail, string password = AdminPassword)
    {
        string? previousEmail = Environment.GetEnvironmentVariable(AdminSeeder.EmailEnvironmentVariable);
        string? previousPassword = Environment.GetEnvironmentVariable(AdminSeeder.PasswordEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(AdminSeeder.EmailEnvironmentVariable, email);
            Environment.SetEnvironmentVariable(AdminSeeder.PasswordEnvironmentVariable, password);

            return await AdminSeeder.SeedAsync(Services, BuildConfiguration(email, password));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AdminSeeder.EmailEnvironmentVariable, previousEmail);
            Environment.SetEnvironmentVariable(AdminSeeder.PasswordEnvironmentVariable, previousPassword);
        }
    }

    /// <summary>Reads the role of the customer that owns the given address.</summary>
    private async Task<UserRole> ReadRoleAsync(string email)
    {
        using IServiceScope scope = Services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;

        IUserRepository users = provider.GetRequiredService<IUserRepository>();
        ISecurityProfileRepository profiles = provider.GetRequiredService<ISecurityProfileRepository>();

        User? user = await users.FindByEmailAsync(email);

        if (user is null)
        {
            return UserRole.User;
        }

        UserSecurityProfile? profile = await profiles.GetByUserIdAsync(user.Id);

        return profile?.Role ?? UserRole.User;
    }

    /// <summary>Creates a customer with the given address and password.</summary>
    private async Task<Guid> CreateCustomerAsync(string email, string password, UserRole role)
    {
        using IServiceScope scope = Services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;

        IUserRepository users = provider.GetRequiredService<IUserRepository>();
        ISecurityProfileRepository profiles = provider.GetRequiredService<ISecurityProfileRepository>();
        IPasswordHasher hasher = provider.GetRequiredService<IPasswordHasher>();
        IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        IClock clock = provider.GetRequiredService<IClock>();

        (string hash, string salt) = hasher.Hash(password);

        User user = new(Guid.NewGuid(), email, "Test customer", hash, salt, clock.UtcNow);

        await users.AddAsync(user);

        await profiles.AddAsync(
            new UserSecurityProfile(
                Guid.NewGuid(),
                user.Id,
                role,
                null,
                null,
                null,
                clock.UtcNow));

        await unitOfWork.SaveChangesAsync();

        return user.Id;
    }

    /// <summary>
    /// The heart of the policy: a customer that already owns the address of the
    /// administrator must never be promoted, even after the configuration changed.
    /// </summary>
    [Fact]
    public async Task OrdinaryCustomerIsNeverPromotedToAdministrator()
    {
        await CreateCustomerAsync(AdminEmail, "CustomerOwnPassword1!", UserRole.User);

        AdminSeedOutcome outcome = await SeedAsync();

        Assert.Equal(AdminSeedOutcome.RefusedExistingAccount, outcome);
        Assert.Equal(UserRole.User, await ReadRoleAsync(AdminEmail));
    }

    /// <summary>
    /// The same attack with a customer that has no security profile at all must be refused too.
    /// </summary>
    [Fact]
    public async Task CustomerWithoutSecurityProfileIsNeverPromotedToAdministrator()
    {
        using (IServiceScope scope = Services.CreateScope())
        {
            IServiceProvider provider = scope.ServiceProvider;

            IUserRepository users = provider.GetRequiredService<IUserRepository>();
            IPasswordHasher hasher = provider.GetRequiredService<IPasswordHasher>();
            IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();
            IClock clock = provider.GetRequiredService<IClock>();

            (string hash, string salt) = hasher.Hash("CustomerOwnPassword1!");

            await users.AddAsync(new User(
                Guid.NewGuid(),
                AdminEmail,
                "Test customer",
                hash,
                salt,
                clock.UtcNow));

            await unitOfWork.SaveChangesAsync();
        }

        AdminSeedOutcome outcome = await SeedAsync();

        Assert.Equal(AdminSeedOutcome.RefusedExistingAccount, outcome);
        Assert.Equal(UserRole.User, await ReadRoleAsync(AdminEmail));
    }

    /// <summary>A customer that owns the address keeps his own password after the refusal.</summary>
    [Fact]
    public async Task RefusedCustomerKeepsHisOwnPassword()
    {
        await CreateCustomerAsync(AdminEmail, "CustomerOwnPassword1!", UserRole.User);

        await SeedAsync();

        using IServiceScope scope = Services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;

        IUserRepository users = provider.GetRequiredService<IUserRepository>();
        IPasswordHasher hasher = provider.GetRequiredService<IPasswordHasher>();

        User? user = await users.FindByEmailAsync(AdminEmail);

        Assert.NotNull(user);
        Assert.True(hasher.Verify("CustomerOwnPassword1!", user.PasswordHash, user.PasswordSalt));
        Assert.False(hasher.Verify(AdminPassword, user.PasswordHash, user.PasswordSalt));
    }

    /// <summary>The administrator is created when the address is free.</summary>
    [Fact]
    public async Task AdministratorIsCreatedWhenTheAddressIsFree()
    {
        AdminSeedOutcome outcome = await SeedAsync();

        Assert.Equal(AdminSeedOutcome.Created, outcome);
        Assert.Equal(UserRole.Admin, await ReadRoleAsync(AdminEmail));
    }

    /// <summary>The second start of the application does not change anything.</summary>
    [Fact]
    public async Task SecondStartKeepsTheAdministratorAndItsPassword()
    {
        await SeedAsync();

        AdminSeedOutcome outcome = await SeedAsync();

        Assert.Equal(AdminSeedOutcome.AlreadyPresent, outcome);
        Assert.Equal(UserRole.Admin, await ReadRoleAsync(AdminEmail));
    }

    /// <summary>
    /// The password of an existing administrator is never reset silently.
    /// The operator is told that the configured password does not match.
    /// </summary>
    [Fact]
    public async Task ExistingAdministratorIsNeverResetSilently()
    {
        await SeedAsync();

        AdminSeedOutcome outcome = await SeedAsync(AdminEmail, "Another_Strong_Password_2026!");

        Assert.Equal(AdminSeedOutcome.PasswordMismatch, outcome);
        Assert.Equal(UserRole.Admin, await ReadRoleAsync(AdminEmail));

        using IServiceScope scope = Services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;

        IUserRepository users = provider.GetRequiredService<IUserRepository>();
        IPasswordHasher hasher = provider.GetRequiredService<IPasswordHasher>();

        User? user = await users.FindByEmailAsync(AdminEmail);

        Assert.NotNull(user);
        Assert.True(hasher.Verify(AdminPassword, user.PasswordHash, user.PasswordSalt));
        Assert.False(hasher.Verify("Another_Strong_Password_2026!", user.PasswordHash, user.PasswordSalt));
    }

    /// <summary>A weak password never becomes the password of the administrator.</summary>
    [Theory]
    [InlineData("short1")]
    [InlineData("12345678901")]
    public async Task WeakPasswordIsRejected(string weakPassword)
    {
        AdminSeedOutcome outcome = await SeedAsync(AdminEmail, weakPassword);

        Assert.Equal(AdminSeedOutcome.RejectedWeakPassword, outcome);
        Assert.Equal(UserRole.User, await ReadRoleAsync(AdminEmail));
    }

    /// <summary>Without the configuration nothing happens at all.</summary>
    [Fact]
    public async Task NothingHappensWithoutCredentials()
    {
        string? previousEmail = Environment.GetEnvironmentVariable(AdminSeeder.EmailEnvironmentVariable);
        string? previousPassword = Environment.GetEnvironmentVariable(AdminSeeder.PasswordEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(AdminSeeder.EmailEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(AdminSeeder.PasswordEnvironmentVariable, null);

            IConfiguration empty = new ConfigurationBuilder().Build();

            AdminSeedOutcome outcome = await AdminSeeder.SeedAsync(Services, empty);

            Assert.Equal(AdminSeedOutcome.NotConfigured, outcome);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AdminSeeder.EmailEnvironmentVariable, previousEmail);
            Environment.SetEnvironmentVariable(AdminSeeder.PasswordEnvironmentVariable, previousPassword);
        }
    }

    /// <summary>Releases the database file.</summary>
    public void Dispose()
    {
        Services.Dispose();

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch (IOException)
        {
            // The file is removed by the operating system later, it does not break the test.
        }
    }
}
