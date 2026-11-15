using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Banking.Dtos;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;

namespace FinanceFlow.Application.Banking;

/// <summary>
/// The use cases of the security of the Demo Bank: the sessions, the history of the sign ins,
/// the audit and the confirmation of the sensitive operations.
/// </summary>
public sealed class SecurityService
{
    /// <summary>The number of minutes a session stays fresh enough for a sensitive operation.</summary>
    public const int StepUpWindowMinutes = 5;

    private readonly ISessionRepository _sessions;
    private readonly ILoginEventRepository _loginEvents;
    private readonly ISecurityAuditRepository _audit;
    private readonly ISecurityProfileRepository _profiles;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Creates the security service.</summary>
    public SecurityService(
        ISessionRepository sessions,
        ILoginEventRepository loginEvents,
        ISecurityAuditRepository audit,
        ISecurityProfileRepository profiles,
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _sessions = sessions;
        _loginEvents = loginEvents;
        _audit = audit;
        _profiles = profiles;
        _users = users;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Opens a session and writes it to the history of the sign ins.</summary>
    public async Task<CustomerSession> StartSessionAsync(
        Guid userId,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.UtcNow;

        CustomerSession session = new(
            Guid.NewGuid(),
            userId,
            now,
            now,
            ipAddress ?? string.Empty,
            userAgent ?? string.Empty,
            DescribeDevice(userAgent),
            null);

        await _sessions.AddAsync(session, cancellationToken);

        await _loginEvents.AddAsync(
            new LoginEvent(
                Guid.NewGuid(),
                userId,
                string.Empty,
                LoginEventKind.SignInSuccess,
                now,
                ipAddress ?? string.Empty,
                session.DeviceName,
                "The customer signed in."),
            cancellationToken);

        await _audit.AddAsync(
            new SecurityAuditLog(
                Guid.NewGuid(),
                userId,
                AuditActionKind.SignIn,
                session.Id.ToString("N"),
                true,
                now,
                ipAddress ?? string.Empty,
                "The customer signed in."),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return session;
    }

    /// <summary>Records a failed attempt to sign in.</summary>
    public async Task RecordFailedSignInAsync(
        string email,
        string ipAddress,
        string userAgent,
        string details,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.UtcNow;

        await _loginEvents.AddAsync(
            new LoginEvent(
                Guid.NewGuid(),
                null,
                MaskEmail(email),
                LoginEventKind.SignInFailed,
                now,
                ipAddress ?? string.Empty,
                DescribeDevice(userAgent),
                details),
            cancellationToken);

        await _audit.AddAsync(
            new SecurityAuditLog(
                Guid.NewGuid(),
                null,
                AuditActionKind.SignIn,
                MaskEmail(email),
                false,
                now,
                ipAddress ?? string.Empty,
                details),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Closes a session and writes it to the history.</summary>
    public async Task EndSessionAsync(
        Guid userId,
        Guid sessionId,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        CustomerSession? session = await _sessions.GetAsync(sessionId, userId, cancellationToken);

        if (session is not null && session.IsActive)
        {
            await _sessions.UpdateAsync(session with { RevokedAtUtc = _clock.UtcNow });
        }

        await _loginEvents.AddAsync(
            new LoginEvent(
                Guid.NewGuid(),
                userId,
                string.Empty,
                LoginEventKind.SignOut,
                _clock.UtcNow,
                ipAddress ?? string.Empty,
                session?.DeviceName ?? string.Empty,
                "The customer signed out."),
            cancellationToken);

        await _audit.AddAsync(
            new SecurityAuditLog(
                Guid.NewGuid(),
                userId,
                AuditActionKind.SignOut,
                sessionId.ToString("N"),
                true,
                _clock.UtcNow,
                ipAddress ?? string.Empty,
                "The customer signed out."),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Returns the sessions of the customer and the history of the sign ins.</summary>
    public async Task<SecurityOverview> GetOverviewAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CustomerSession> sessions = await _sessions.GetForUserAsync(userId, cancellationToken);
        IReadOnlyList<LoginEvent> events = await _loginEvents.GetForUserAsync(userId, 50, cancellationToken);

        UserSecurityProfile? profile = await _profiles.GetByUserIdAsync(userId, cancellationToken);

        return new SecurityOverview(
            sessions
                .Select(session => new SessionItem(
                    session.Id,
                    session.DeviceName,
                    session.IpAddress,
                    session.CreatedAtUtc,
                    session.LastSeenAtUtc,
                    session.IsActive))
                .ToList(),
            events
                .Select(loginEvent => new LoginEventItem(
                    loginEvent.Kind,
                    loginEvent.OccurredAtUtc,
                    loginEvent.IpAddress,
                    loginEvent.DeviceName,
                    loginEvent.Details))
                .ToList(),
            profile?.HasTransactionPin ?? false,
            profile?.HasTotp ?? false);
    }

    /// <summary>Terminates one session of the customer.</summary>
    public async Task<Result> TerminateSessionAsync(
        Guid userId,
        Guid sessionId,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        CustomerSession? session = await _sessions.GetAsync(sessionId, userId, cancellationToken);

        if (session is null)
        {
            return new Result.Failure("The session does not exist.")
            {
                Code = "session_not_found"
            };
        }

        if (!session.IsActive)
        {
            return new Result.Failure("The session was already terminated.")
            {
                Code = "session_closed"
            };
        }

        await _sessions.UpdateAsync(session with { RevokedAtUtc = _clock.UtcNow });

        await _loginEvents.AddAsync(
            new LoginEvent(
                Guid.NewGuid(),
                userId,
                string.Empty,
                LoginEventKind.SessionTerminated,
                _clock.UtcNow,
                ipAddress ?? string.Empty,
                session.DeviceName,
                "The session was terminated."),
            cancellationToken);

        await _audit.AddAsync(
            new SecurityAuditLog(
                Guid.NewGuid(),
                userId,
                AuditActionKind.SessionTerminated,
                sessionId.ToString("N"),
                true,
                _clock.UtcNow,
                ipAddress ?? string.Empty,
                "The customer terminated a session."),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success.Instance;
    }

    /// <summary>
    /// Sets or changes the transaction PIN of the customer.
    /// The PIN is stored with the same hashing as the password, it is never written in plain text.
    /// </summary>
    public async Task<Result> SetTransactionPinAsync(
        Guid userId,
        string pin,
        string currentPassword,
        CancellationToken cancellationToken = default)
    {
        UserSecurityProfile? profile = await _profiles.GetByUserIdAsync(userId, cancellationToken);

        if (profile is null)
        {
            return new Result.Failure("The security profile does not exist.")
            {
                Code = "profile_not_found"
            };
        }

        User? user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null || !_passwordHasher.Verify(currentPassword, user.PasswordHash, user.PasswordSalt))
        {
            await WriteAuditAsync(userId, AuditActionKind.PinChanged, "pin", false, "The password was wrong.");
            return new Result.Failure("The password is wrong.")
            {
                Code = "invalid_credentials"
            };
        }

        if (string.IsNullOrWhiteSpace(pin) || pin.Trim().Length < 4 || pin.Trim().Length > 8 || !pin.Trim().All(char.IsDigit))
        {
            return new Result.Failure("The PIN must have between 4 and 8 digits.")
            {
                Code = "validation"
            };
        }

        (string hash, string salt) = _passwordHasher.Hash(pin.Trim());

        await _profiles.UpdateAsync(profile with
        {
            TransactionPinHash = hash,
            TransactionPinSalt = salt
        });

        await WriteAuditAsync(userId, AuditActionKind.PinChanged, "pin", true, "The transaction PIN was changed.");

        return Result.Success.Instance;
    }

    /// <summary>Checks what the customer has to do to confirm a sensitive operation.</summary>
    public async Task<StepUpRequirement> GetRequirementAsync(
        Guid userId,
        DateTimeOffset? authenticatedAt,
        CancellationToken cancellationToken = default)
    {
        UserSecurityProfile? profile = await _profiles.GetByUserIdAsync(userId, cancellationToken);

        if (profile is null)
        {
            return new StepUpRequirement(true, false, "The security profile does not exist.");
        }

        if (profile.HasTransactionPin)
        {
            return new StepUpRequirement(true, false, "Please confirm the operation with your transaction PIN.");
        }

        bool fresh = authenticatedAt is not null &&
                     (_clock.UtcNow - authenticatedAt.Value).TotalMinutes <= StepUpWindowMinutes;

        return fresh
            ? new StepUpRequirement(false, false, "The session is still valid.")
            : new StepUpRequirement(false, true, "Please confirm the operation with your password.");
    }

    /// <summary>Checks the transaction PIN of the customer.</summary>
    public async Task<Result> VerifyTransactionPinAsync(
        Guid userId,
        string pin,
        CancellationToken cancellationToken = default)
    {
        UserSecurityProfile? profile = await _profiles.GetByUserIdAsync(userId, cancellationToken);

        if (profile is null || !profile.HasTransactionPin)
        {
            return new Result.Failure("No transaction PIN is set.")
            {
                Code = "no_pin"
            };
        }

        if (string.IsNullOrWhiteSpace(pin) || !_passwordHasher.Verify(pin.Trim(), profile.TransactionPinHash!, profile.TransactionPinSalt!))
        {
            await WriteAuditAsync(userId, AuditActionKind.StepUp, "transfer", false, "The transaction PIN was wrong.");

            return new Result.Failure("The transaction PIN is wrong.")
            {
                Code = "invalid_pin"
            };
        }

        await WriteAuditAsync(userId, AuditActionKind.StepUp, "transfer", true, "The operation was confirmed with the PIN.");

        return Result.Success.Instance;
    }

    /// <summary>Checks the password of the customer, it is the confirmation of a sensitive operation.</summary>
    public async Task<Result> VerifyPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null || !_passwordHasher.Verify(password ?? string.Empty, user.PasswordHash, user.PasswordSalt))
        {
            await WriteAuditAsync(userId, AuditActionKind.StepUp, "transfer", false, "The password was wrong.");

            return new Result.Failure("The password is wrong.")
            {
                Code = "invalid_credentials"
            };
        }

        await WriteAuditAsync(userId, AuditActionKind.StepUp, "transfer", true, "The operation was confirmed with the password.");

        return Result.Success.Instance;
    }

    /// <summary>Enables or disables the authenticator of the customer.</summary>
    public async Task<Result<string>> EnableTotpAsync(
        Guid userId,
        string currentPassword,
        CancellationToken cancellationToken = default)
    {
        UserSecurityProfile? profile = await _profiles.GetByUserIdAsync(userId, cancellationToken);

        if (profile is null)
        {
            return new Fail<string>("The security profile does not exist.")
            {
                Code = "profile_not_found"
            };
        }

        User? user = await _users.FindByIdAsync(userId, cancellationToken);

        if (user is null || !_passwordHasher.Verify(currentPassword, user.PasswordHash, user.PasswordSalt))
        {
            return new Fail<string>("The password is wrong.")
            {
                Code = "invalid_credentials"
            };
        }

        string secret = Totp.GenerateSecret();

        await _profiles.UpdateAsync(profile with { TotpSecret = secret });

        await WriteAuditAsync(userId, AuditActionKind.TotpChanged, "totp", true, "The authenticator was enabled.");

        return new Ok<string>(Totp.FormatSecret(secret));
    }

    /// <summary>Checks the code of the authenticator of the customer.</summary>
    public async Task<Result> VerifyTotpAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken = default)
    {
        UserSecurityProfile? profile = await _profiles.GetByUserIdAsync(userId, cancellationToken);

        if (profile is null || !profile.HasTotp)
        {
            return new Result.Failure("The authenticator is not enabled.")
            {
                Code = "no_totp"
            };
        }

        if (!Totp.VerifyCode(profile.TotpSecret!, code, _clock.UtcNow))
        {
            return new Result.Failure("The code of the authenticator is wrong.")
            {
                Code = "invalid_totp"
            };
        }

        return Result.Success.Instance;
    }

    /// <summary>Writes an entry into the security audit.</summary>
    public async Task WriteAuditAsync(
        Guid? actorUserId,
        AuditActionKind action,
        string target,
        bool succeeded,
        string details,
        CancellationToken cancellationToken = default)
    {
        await _audit.AddAsync(
            new SecurityAuditLog(
                Guid.NewGuid(),
                actorUserId,
                action,
                target,
                succeeded,
                _clock.UtcNow,
                string.Empty,
                details),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Hides identifying characters in an e-mail address or international phone number.</summary>
    public static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        string candidate = email.Trim();
        if (candidate.StartsWith('+'))
        {
            string digits = new(candidate.Where(char.IsAsciiDigit).ToArray());
            return digits.Length <= 4
                ? $"+{new string('*', digits.Length)}"
                : $"+{new string('*', digits.Length - 4)}{digits[^4..]}";
        }

        int at = candidate.IndexOf('@', StringComparison.Ordinal);

        if (at <= 1)
        {
            return candidate;
        }

        string local = candidate[..at];

        return $"{local[..1]}{new string('*', Math.Max(local.Length - 1, 1))}{candidate[at..]}";
    }

    /// <summary>Builds a short name of the device out of the browser.</summary>
    public static string DescribeDevice(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "Unknown device";
        }

        string agent = userAgent;

        string browser = agent.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge"
            : agent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome"
            : agent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox"
            : agent.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari"
            : "Browser";

        string system = agent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
            : agent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
            : agent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone"
            : agent.Contains("Mac", StringComparison.OrdinalIgnoreCase) ? "macOS"
            : agent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux"
            : string.Empty;

        return string.IsNullOrEmpty(system) ? browser : $"{browser} on {system}";
    }
}
