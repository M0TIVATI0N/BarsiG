using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Banking.Dtos;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.Application.Banking;

/// <summary>
/// The use case of a transfer between two customers of the Demo Bank.
/// The service owns the rules, the executor owns the atomicity: the service never writes
/// to the ledger itself, it only decides what has to happen.
/// </summary>
public sealed class TransferService
{
    private readonly IBankAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly ILedgerExecutor _executor;
    private readonly IUserDirectory _directory;
    private readonly IDataChangeNotifier _notifier;
    private readonly PhoneRecipientLookupThrottle _phoneLookupThrottle;
    private readonly BankingOptions _options;
    private readonly IClock _clock;

    /// <summary>Creates the transfer service.</summary>
    public TransferService(
        IBankAccountRepository accounts,
        ILedgerRepository ledger,
        ILedgerExecutor executor,
        IUserDirectory directory,
        IDataChangeNotifier notifier,
        PhoneRecipientLookupThrottle phoneLookupThrottle,
        BankingOptions options,
        IClock clock)
    {
        _accounts = accounts;
        _ledger = ledger;
        _executor = executor;
        _directory = directory;
        _notifier = notifier;
        _phoneLookupThrottle = phoneLookupThrottle;
        _options = options;
        _clock = clock;
    }

    /// <summary>Finds the customers that may receive a transfer.</summary>
    public async Task<IReadOnlyList<RecipientSuggestion>> SearchRecipientsAsync(
        string query,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _directory.SearchAsync(query ?? string.Empty, userId, 20, cancellationToken);

    /// <summary>
    /// Resolves an exact E.164 phone number to a safe preview. Only verified, eligible
    /// identities are returned and each customer is limited to eight lookups per minute.
    /// </summary>
    public async Task<Result<PhoneRecipientPreview>> FindRecipientByPhoneAsync(
        Guid senderUserId,
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        if (!_phoneLookupThrottle.TryAcquire(senderUserId, _clock.UtcNow))
        {
            return new Fail<PhoneRecipientPreview>("Too many recipient searches. Please wait a minute and try again.")
            {
                Code = "recipient_lookup_limited"
            };
        }

        Result<string> normalized = Validation.PhoneNumber(phoneNumber, "PhoneNumber");

        if (normalized.IsFailure())
        {
            return new Fail<PhoneRecipientPreview>(normalized.Error)
            {
                Code = ((Fail<string>)normalized).Code
            };
        }

        string phone = normalized.ValueOr(string.Empty);
        RecipientSuggestion? recipient = await _directory.FindByVerifiedPhoneNumberAsync(
            phone,
            _options.AllowLocalDemoPhoneRecipients,
            cancellationToken);

        if (recipient is null)
        {
            return new Fail<PhoneRecipientPreview>("No eligible recipient was found for this phone number.")
            {
                Code = "recipient_not_found"
            };
        }

        if (recipient.UserId == senderUserId)
        {
            return new Fail<PhoneRecipientPreview>("You cannot transfer money to your own account.")
            {
                Code = "same_user"
            };
        }

        BankAccount? account = await _accounts.GetPrimaryAsync(recipient.UserId, cancellationToken);

        if (account is null || account.Status != BankAccountStatus.Active)
        {
            return new Fail<PhoneRecipientPreview>("No eligible recipient was found for this phone number.")
            {
                Code = "recipient_not_found"
            };
        }

        return new Ok<PhoneRecipientPreview>(new PhoneRecipientPreview(
            recipient.UserId,
            recipient.DisplayName,
            MaskPhone(phone),
            MaskAccountNumber(account.AccountNumber)));
    }

    /// <summary>Finds one recipient by the exact e-mail address, for the confirmation step.</summary>
    public async Task<Result<RecipientSuggestion>> ResolveRecipientAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        RecipientSuggestion? recipient = await _directory.FindByEmailAsync(email ?? string.Empty, cancellationToken);

        if (recipient is null)
        {
            return new Fail<RecipientSuggestion>("No customer of the Demo Bank uses this e-mail address.")
            {
                Code = "recipient_not_found"
            };
        }

        return new Ok<RecipientSuggestion>(recipient);
    }

    /// <summary>
    /// Checks a transfer and returns the details the user has to confirm.
    /// Nothing is moved here, the method only reads the data.
    /// </summary>
    public async Task<Result<TransferPreview>> PreviewAsync(
        Guid userId,
        TransferRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        BankAccount? sender = await _accounts.GetPrimaryAsync(userId, cancellationToken);

        if (sender is null)
        {
            return new Fail<TransferPreview>("The primary account does not exist.")
            {
                Code = "account_not_found"
            };
        }

        if (sender.Status != BankAccountStatus.Active)
        {
            return new Fail<TransferPreview>("The primary account is not active.")
            {
                Code = "account_not_active"
            };
        }

        string? recipientPhone = null;

        if (request.RecipientPhoneNumber is not null)
        {
            Result<string> normalizedPhone = Validation.PhoneNumber(request.RecipientPhoneNumber, "RecipientPhoneNumber");

            if (normalizedPhone.IsFailure())
            {
                return new Fail<TransferPreview>(normalizedPhone.Error)
                {
                    Code = ((Fail<string>)normalizedPhone).Code
                };
            }

            recipientPhone = normalizedPhone.ValueOr(string.Empty);
            RecipientSuggestion? verifiedRecipient = await _directory.FindByVerifiedPhoneNumberAsync(
                recipientPhone,
                _options.AllowLocalDemoPhoneRecipients,
                cancellationToken);

            if (verifiedRecipient is null || verifiedRecipient.UserId != request.RecipientUserId)
            {
                return new Fail<TransferPreview>("No eligible recipient was found for this phone number.")
                {
                    Code = "recipient_not_found"
                };
            }
        }

        if (request.RecipientUserId == userId)
        {
            return new Fail<TransferPreview>("The recipient must be somebody else.")
            {
                Code = "same_user"
            };
        }

        BankAccount? recipient = await _accounts.GetPrimaryAsync(request.RecipientUserId, cancellationToken);

        if (recipient is null)
        {
            return new Fail<TransferPreview>("The recipient does not have a primary account.")
            {
                Code = "recipient_not_found"
            };
        }

        if (recipient.Status != BankAccountStatus.Active)
        {
            return new Fail<TransferPreview>("The account of the recipient is not active.")
            {
                Code = "recipient_not_active"
            };
        }

        if (!CurrenciesMatch(sender.Currency, recipient.Currency))
        {
            return new Fail<TransferPreview>("Transfers require accounts with the same currency.")
            {
                Code = "currency_mismatch"
            };
        }

        Money amount = Money.FromDecimal(request.Amount);

        Result<Money> amountCheck = BankingMath.ValidateAmount(amount, _options.Limits);

        if (amountCheck.IsFailure())
        {
            return new Fail<TransferPreview>(amountCheck.Error) { Code = ((Fail<Money>)amountCheck).Code };
        }

        Money transferredToday = await GetTransferredTodayAsync(userId, cancellationToken);
        Result<Money> dailyCheck = BankingMath.ValidateDailyLimit(transferredToday, amount, _options.Limits);

        if (dailyCheck.IsFailure())
        {
            return new Fail<TransferPreview>(dailyCheck.Error) { Code = ((Fail<Money>)dailyCheck).Code };
        }

        if (sender.Balance < amount)
        {
            return new Fail<TransferPreview>("There is not enough money on the account.")
            {
                Code = "insufficient_funds"
            };
        }

        RecipientSuggestion? suggestion = recipientPhone is not null || recipient.UserId is null
            ? null
            : await _directory.FindByUserIdAsync(recipient.UserId.Value, cancellationToken);

        string description = string.IsNullOrWhiteSpace(request.Description)
            ? $"Transfer to {recipient.DisplayName}"
            : request.Description.Trim();

        return new Ok<TransferPreview>(new TransferPreview(
            sender.DisplayName,
            sender.AccountNumber,
            sender.Balance,
            recipient.DisplayName,
            recipientPhone is null ? recipient.AccountNumber : MaskAccountNumber(recipient.AccountNumber),
            suggestion?.MaskedEmail ?? string.Empty,
            amount,
            sender.Currency,
            description,
            sender.Balance - amount,
            recipientPhone is null ? string.Empty : MaskPhone(recipientPhone)));
    }

    /// <summary>
    /// Executes a confirmed transfer.
    /// The money is moved by the executor inside one transaction, a repeated request with the
    /// same idempotency key never moves the money twice.
    /// </summary>
    public async Task<Result<TransferOutcome>> ExecuteAsync(
        Guid userId,
        TransferRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<TransferPreview> preview = await PreviewAsync(userId, request, cancellationToken);

        if (preview.IsFailure())
        {
            return new Fail<TransferOutcome>(preview.Error) { Code = ((Fail<TransferPreview>)preview).Code };
        }

        TransferPreview details = preview.ValueOr(default!);

        BankAccount? sender = await _accounts.GetPrimaryAsync(userId, cancellationToken);
        BankAccount? recipient = await _accounts.GetPrimaryAsync(request.RecipientUserId, cancellationToken);

        if (sender is null || recipient is null)
        {
            return new Fail<TransferOutcome>("The account does not exist.")
            {
                Code = "account_not_found"
            };
        }

        string key = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? Guid.NewGuid().ToString()
            : request.IdempotencyKey.Trim();
        string? expectedRecipientPhone = request.RecipientPhoneNumber is null
            ? null
            : Validation.PhoneNumber(request.RecipientPhoneNumber, "RecipientPhoneNumber").ValueOr(string.Empty);

        Result<LedgerTransfer> executed = await _executor.ExecuteAsync(
            new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                recipient.Id,
                userId,
                details.Amount,
                details.Description,
                key,
                expectedRecipientPhone),
            cancellationToken);

        if (executed.IsFailure())
        {
            return new Fail<TransferOutcome>(executed.Error) { Code = ((Fail<LedgerTransfer>)executed).Code };
        }

        LedgerTransfer transfer = executed.ValueOr(default!);

        Money senderBalance = await ReadBalanceAsync(sender.Id, cancellationToken);
        Money recipientBalance = await ReadBalanceAsync(recipient.Id, cancellationToken);

        await _notifier.NotifyAsync(userId, "banking", "transfer", cancellationToken);
        await _notifier.NotifyAsync(recipient.UserId ?? Guid.Empty, "banking", "transfer", cancellationToken);

        return new Ok<TransferOutcome>(new TransferOutcome(
            transfer.Id,
            details.Amount,
            senderBalance,
            recipientBalance,
            details.RecipientName,
            transfer.CompletedAtUtc ?? _clock.UtcNow));
    }

    /// <summary>Returns the total amount the user has already transferred today.</summary>
    private async Task<Money> GetTransferredTodayAsync(Guid userId, CancellationToken cancellationToken)
    {
        DateTimeOffset fromUtcInclusive = new(
            _clock.Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        DateTimeOffset toUtcExclusive = fromUtcInclusive.AddDays(1);

        return await _ledger.GetCompletedUserTransferTotalAsync(
            userId,
            fromUtcInclusive,
            toUtcExclusive,
            cancellationToken);
    }

    /// <summary>Reads the current balance of an account.</summary>
    private async Task<Money> ReadBalanceAsync(Guid accountId, CancellationToken cancellationToken)
    {
        BankAccount? account = await _accounts.GetByIdAsync(accountId, cancellationToken);

        return account?.Balance ?? Money.Zero;
    }

    private static string MaskPhone(string phoneNumber)
    {
        string normalized = phoneNumber.Trim();

        if (normalized.Length <= 7)
        {
            return normalized[..1] + new string('•', normalized.Length - 1);
        }

        return $"{normalized[..2]} {new string('•', normalized.Length - 6)} {normalized[^4..]}";
    }

    private static string MaskAccountNumber(string accountNumber) =>
        accountNumber.Length <= 4
            ? "Account ending in ••••"
            : $"Account ending in {accountNumber[^4..]}";

    private static bool CurrenciesMatch(string first, string second) =>
        !string.IsNullOrWhiteSpace(first) &&
        !string.IsNullOrWhiteSpace(second) &&
        string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
}
