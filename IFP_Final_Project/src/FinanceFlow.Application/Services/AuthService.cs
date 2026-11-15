using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Dtos;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.Application.Services;

/// <summary>
/// Registration and sign in.
/// The service never throws for a wrong input: every expected problem is returned as a
/// <see cref="Result{T}"/> so the interface layer can show it to the user.
/// </summary>
public sealed class AuthService
{
    private const string PhoneOnlyEmailDomain = "@phone.invalid";

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly FinanceFlow.Application.Banking.AccountService _accounts;
    private readonly ISecurityProfileRepository _profiles;

    /// <summary>Creates the authentication service.</summary>
    public AuthService(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork,
        IClock clock,
        FinanceFlow.Application.Banking.AccountService accounts,
        ISecurityProfileRepository profiles)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _accounts = accounts;
        _profiles = profiles;
    }

    /// <summary>Registers a new account and returns the session of the new user.</summary>
    public async Task<Result<UserSession>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<string>? email = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : Validation.EmailAddress(request.Email, "Email");
        Result<string> phone = Validation.KazakhstanPhoneNumber(request.PhoneNumber, "PhoneNumber");
        Result<string> displayName = Validation.Required(
            request.DisplayName,
            "DisplayName",
            "The display name is required.");
        Result<string> password = Validation.Password(request.Password, "Password");

        ValidationBuilder builder = new ValidationBuilder()
            .Add(displayName)
            .Add(password);

        if (email is not null)
        {
            builder.Add(email);
        }

        builder.Add(phone);

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            builder.AddError("ConfirmPassword", "The passwords do not match.");
        }

        if (!builder.IsValid)
        {
            return builder.Failure<UserSession>();
        }

        string normalizedEmail = email?.ValueOr(string.Empty) ?? string.Empty;
        string normalizedPhone = phone.ValueOr(string.Empty);
        Guid userId = Guid.NewGuid();
        bool hasEmailLogin = !string.IsNullOrWhiteSpace(normalizedEmail);

        if (hasEmailLogin && normalizedEmail.EndsWith(PhoneOnlyEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            return new Fail<UserSession>("This e-mail address cannot be used.")
            {
                Code = "reserved_email_domain",
                Details = [new ValidationError("Email", "This e-mail address cannot be used.")]
            };
        }

        if (hasEmailLogin &&
            await _users.ExistsAsync(normalizedEmail, cancellationToken))
        {
            return new Fail<UserSession>("An account with this e-mail address already exists.")
            {
                Code = "duplicate_email",
                Details = [new ValidationError("Email", "An account with this e-mail address already exists.")]
            };
        }

        if (await _users.PhoneNumberExistsAsync(normalizedPhone, cancellationToken))
        {
            return new Fail<UserSession>("An account with this phone number already exists.")
            {
                Code = "duplicate_phone",
                Details = [new ValidationError("PhoneNumber", "An account with this phone number already exists.")]
            };
        }

        if (!hasEmailLogin)
        {
            // User.Email remains non-null and uniquely indexed for compatibility with the
            // current schema. Phone-only identities receive a unique non-routable alias.
            normalizedEmail = $"phone-{userId:N}{PhoneOnlyEmailDomain}";
        }

        (string hash, string salt) = _passwordHasher.Hash(password.ValueOr(string.Empty));

        User user = new(
            userId,
            normalizedEmail,
            displayName.ValueOr(string.Empty),
            hash,
            salt,
            _clock.UtcNow,
            normalizedPhone,
            IsPhoneVerified: false);

        await _users.AddAsync(user, cancellationToken);

        // Every new customer of the Demo Bank receives one primary account with exactly zero.
        await _accounts.CreatePrimaryAccountAsync(user.Id, cancellationToken);

        await _profiles.AddAsync(
            new FinanceFlow.Domain.Banking.UserSecurityProfile(
                Guid.NewGuid(),
                user.Id,
                FinanceFlow.Domain.Banking.UserRole.User,
                null,
                null,
                null,
                _clock.UtcNow),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new Ok<UserSession>(new UserSession(
            user.Id,
            hasEmailLogin ? user.Email : string.Empty,
            user.DisplayName));
    }

    /// <summary>Checks the credentials and returns the session of the user.</summary>
    public Task<Result<UserSession>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default) =>
        AuthenticateAsync(request, allowLocalDemoPhone: false, cancellationToken);

    /// <summary>Checks credentials, optionally allowing the explicitly local demo verification state.</summary>
    public async Task<Result<UserSession>> AuthenticateAsync(
        LoginRequest request,
        bool allowLocalDemoPhone = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string identifier = request.Email?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return CredentialFailure("missing_identifier");
        }

        bool usingPhone = !identifier.Contains('@') &&
            (identifier.StartsWith('+') || identifier.Any(char.IsAsciiDigit));
        if (!usingPhone && IsPhoneOnlyEmail(identifier))
        {
            return CredentialFailure("reserved_phone_identity_alias");
        }

        User? user;

        if (usingPhone)
        {
            Result<string> normalizedPhone = Validation.PhoneNumber(identifier, "Identifier");
            if (normalizedPhone.IsFailure())
            {
                return CredentialFailure("phone_identifier_invalid");
            }

            user = await _users.FindByPhoneNumberAsync(
                normalizedPhone.ValueOr(string.Empty),
                cancellationToken);
        }
        else
        {
            user = await _users.FindByEmailAsync(identifier, cancellationToken);
        }

        if (user is null)
        {
            return CredentialFailure(usingPhone ? "phone_lookup_miss" : "email_lookup_miss");
        }

        if (usingPhone && !user.IsPhoneVerified)
        {
            return CredentialFailure("phone_not_verified");
        }

        if (usingPhone &&
            (user.PhoneVerificationMethod == FinanceFlow.Domain.Enums.PhoneVerificationMethod.None ||
             (user.PhoneVerificationMethod == FinanceFlow.Domain.Enums.PhoneVerificationMethod.LocalDemo && !allowLocalDemoPhone)))
        {
            return CredentialFailure("verification_method_not_allowed");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return CredentialFailure("missing_password");
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
        {
            return CredentialFailure("password_mismatch");
        }

        // An unverified phone must not be bypassed by choosing the e-mail sign-in path.
        // Legacy accounts without a phone remain usable; administrators may have none.
        if (!usingPhone && user.PhoneNumber is not null && !user.IsPhoneVerified)
        {
            return CredentialFailure("phone_not_verified");
        }

        FinanceFlow.Domain.Banking.UserSecurityProfile? profile =
            await _profiles.GetByUserIdAsync(user.Id, cancellationToken);

        string role = profile?.Role == FinanceFlow.Domain.Banking.UserRole.Admin ? "Admin" : "User";

        return new Ok<UserSession>(new UserSession(
            user.Id,
            IsPhoneOnlyEmail(user.Email) ? string.Empty : user.Email,
            user.DisplayName,
            role));
    }

    /// <summary>Returns the registered phone for a just-created account without exposing it publicly.</summary>
    public async Task<string?> GetPhoneNumberAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        User? user = await _users.FindByIdAsync(userId, cancellationToken);
        return user?.PhoneNumber;
    }

    /// <summary>
    /// Finds a pending registration only after its password has been verified.
    /// This supports safe OTP recovery without creating a second user or banking account.
    /// </summary>
    public async Task<Guid?> TryGetPendingPhoneRegistrationAsync(
        string phoneNumber,
        string password,
        CancellationToken cancellationToken = default)
    {
        Result<string> normalized = Validation.KazakhstanPhoneNumber(phoneNumber, "PhoneNumber");

        if (normalized.IsFailure() || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        User? user = await _users.FindByPhoneNumberAsync(
            normalized.ValueOr(string.Empty),
            cancellationToken);

        if (user is null ||
            user.IsPhoneVerified ||
            user.PhoneVerificationMethod != FinanceFlow.Domain.Enums.PhoneVerificationMethod.None ||
            !_passwordHasher.Verify(password, user.PasswordHash, user.PasswordSalt))
        {
            return null;
        }

        return user.Id;
    }

    private static Fail<UserSession> CredentialFailure(string diagnosticCode) =>
        new("The e-mail address or the password is wrong.")
        {
            // Internal result code; the HTTP endpoint continues returning the same generic
            // message so callers cannot use it to enumerate accounts or verification state.
            Code = diagnosticCode
        };

    private static bool IsPhoneOnlyEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ||
        email.EndsWith(PhoneOnlyEmailDomain, StringComparison.OrdinalIgnoreCase);
}
