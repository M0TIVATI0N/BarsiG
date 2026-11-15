using FinanceFlow.Application.Abstractions;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Entities;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinanceFlow.Infrastructure.Seeding;

/// <summary>
/// The result of one attempt to bootstrap the administrator of the Demo Bank.
/// </summary>
public enum AdminSeedOutcome
{
    /// <summary>No credentials were configured, nothing was done.</summary>
    NotConfigured = 0,

    /// <summary>The administrator was created, it did not exist before.</summary>
    Created = 1,

    /// <summary>The administrator already existed and was left untouched.</summary>
    AlreadyPresent = 2,

    /// <summary>
    /// The administrator already existed, but the configured password does not match
    /// the stored one. The stored password was kept, nothing was reset.
    /// </summary>
    PasswordMismatch = 3,

    /// <summary>
    /// A normal account already owns the configured e-mail address. The account was
    /// left untouched: an ordinary customer is never promoted to administrator.
    /// </summary>
    RefusedExistingAccount = 4,

    /// <summary>The configured password is too weak to protect the Demo Bank.</summary>
    RejectedWeakPassword = 5
}

/// <summary>
/// Creates the administrator of the Demo Bank.
/// The credentials are never stored in the source code: they come from the environment
/// variables <c>BARSIG_ADMIN_EMAIL</c> and <c>BARSIG_ADMIN_PASSWORD</c> or from the local
/// configuration file that is excluded from the repository.
/// </summary>
/// <remarks>
/// <para>
/// The bootstrap policy is deliberately narrow: the seeder may only create a brand new
/// administrator. It never changes the role of an account that already exists, because
/// anybody could register the configured e-mail address first and would then receive
/// the rights of the administrator. It also never resets a stored password.
/// </para>
/// </remarks>
public static class AdminSeeder
{
    /// <summary>The environment variable that holds the e-mail address of the administrator.</summary>
    public const string EmailEnvironmentVariable = "BARSIG_ADMIN_EMAIL";

    /// <summary>The environment variable that holds the password of the administrator.</summary>
    public const string PasswordEnvironmentVariable = "BARSIG_ADMIN_PASSWORD";

    /// <summary>The minimal length of the password of the administrator.</summary>
    public const int MinimumPasswordLength = 12;

    /// <summary>Creates the administrator when the credentials are configured.</summary>
    /// <returns>The outcome of the attempt, so that the caller can report it.</returns>
    public static async Task<AdminSeedOutcome> SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string? email = Environment.GetEnvironmentVariable(EmailEnvironmentVariable)
            ?? configuration[EmailEnvironmentVariable]
            ?? configuration["Banking:Admin:Email"];

        string? password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable)
            ?? configuration[PasswordEnvironmentVariable]
            ?? configuration["Banking:Admin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return AdminSeedOutcome.NotConfigured;
        }

        if (password.Length < MinimumPasswordLength)
        {
            using IServiceScope weakScope = services.CreateScope();

            weakScope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(AdminSeeder))
                .LogError(
                    "The administrator of the Demo Bank was not created: the configured password " +
                    "is shorter than {MinimumLength} characters.",
                    MinimumPasswordLength);

            return AdminSeedOutcome.RejectedWeakPassword;
        }

        using IServiceScope scope = services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;

        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminSeeder));
        IUserRepository users = provider.GetRequiredService<IUserRepository>();
        ISecurityProfileRepository profiles = provider.GetRequiredService<ISecurityProfileRepository>();
        IPasswordHasher hasher = provider.GetRequiredService<IPasswordHasher>();
        IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        IClock clock = provider.GetRequiredService<IClock>();

        string normalized = email.Trim().ToLowerInvariant();
        User? existing = await users.FindByEmailAsync(normalized, cancellationToken);

        if (existing is null)
        {
            (string hash, string salt) = hasher.Hash(password);

            User administrator = new(
                Guid.NewGuid(),
                normalized,
                "Demo Bank administrator",
                hash,
                salt,
                clock.UtcNow);

            // The customer and the profile are written by one transaction, so the
            // administrator can never exist without the role that belongs to it.
            await users.AddAsync(administrator, cancellationToken);

            await profiles.AddAsync(
                new UserSecurityProfile(
                    Guid.NewGuid(),
                    administrator.Id,
                    UserRole.Admin,
                    null,
                    null,
                    null,
                    clock.UtcNow),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation("The administrator {Email} of the Demo Bank was created.", normalized);

            return AdminSeedOutcome.Created;
        }

        UserSecurityProfile? profile = await profiles.GetByUserIdAsync(existing.Id, cancellationToken);

        if (profile?.Role != UserRole.Admin)
        {
            // The account exists and it is not an administrator. Promoting it would give
            // the rights of the bank to whoever managed to register this address first.
            logger.LogWarning(
                "The e-mail address {Email} already belongs to an account that is not an " +
                "administrator. The account was left untouched and it was not promoted. " +
                "Choose another address for the administrator of the Demo Bank.",
                normalized);

            return AdminSeedOutcome.RefusedExistingAccount;
        }

        // The administrator already exists. Its credentials are never reset silently.
        if (!hasher.Verify(password, existing.PasswordHash, existing.PasswordSalt))
        {
            logger.LogWarning(
                "The administrator {Email} of the Demo Bank already exists, but the configured " +
                "password does not match the stored one. The stored password was kept. Use the " +
                "password rotation of the security page to change it.",
                normalized);

            return AdminSeedOutcome.PasswordMismatch;
        }

        logger.LogInformation("The administrator {Email} of the Demo Bank already exists.", normalized);

        return AdminSeedOutcome.AlreadyPresent;
    }
}
