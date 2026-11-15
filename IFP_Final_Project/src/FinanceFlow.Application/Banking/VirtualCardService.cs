using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Banking.Dtos;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;

namespace FinanceFlow.Application.Banking;

/// <summary>
/// The use cases of the virtual cards of the Demo Bank.
/// A card is only a label of an account: it never reaches a payment network and it can
/// only be changed by the customer who owns it.
/// </summary>
public sealed class VirtualCardService
{
    /// <summary>The fictional prefix of every card number of the Demo Bank.</summary>
    public const string FictionalPrefix = "9999";

    private readonly IVirtualCardRepository _cards;
    private readonly IBankAccountRepository _accounts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Creates the virtual card service.</summary>
    public VirtualCardService(
        IVirtualCardRepository cards,
        IBankAccountRepository accounts,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _cards = cards;
        _accounts = accounts;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Returns every card of the customer.</summary>
    public async Task<IReadOnlyList<VirtualCardItem>> GetCardsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<VirtualCard> cards = await _cards.GetForUserAsync(userId, cancellationToken);

        return cards.Select(card => ToItem(card)).ToList();
    }

    /// <summary>Creates a new virtual card for one of the accounts of the customer.</summary>
    public async Task<Result<VirtualCardItem>> CreateAsync(
        Guid userId,
        Guid linkedAccountId,
        string nickname,
        decimal dailyLimit,
        decimal monthlyLimit,
        CancellationToken cancellationToken = default)
    {
        BankAccount? account = await _accounts.GetOwnedAsync(linkedAccountId, userId, cancellationToken);

        if (account is null)
        {
            return new Fail<VirtualCardItem>("The account does not exist.")
            {
                Code = "account_not_found"
            };
        }

        if (account.Status != BankAccountStatus.Active)
        {
            return new Fail<VirtualCardItem>("The account is not active.")
            {
                Code = "account_not_active"
            };
        }

        string title = (nickname ?? string.Empty).Trim();

        if (title.Length == 0)
        {
            title = "BarsiG card";
        }

        string lastFour = Random.Shared.Next(0, 10000).ToString("D4");

        VirtualCard card = new(
            Guid.NewGuid(),
            userId,
            linkedAccountId,
            title,
            $"•••• •••• •••• {lastFour}",
            lastFour,
            VirtualCardStatus.Active,
            Money.FromDecimal(dailyLimit < 0m ? 0m : dailyLimit),
            Money.FromDecimal(monthlyLimit < 0m ? 0m : monthlyLimit),
            _clock.UtcNow,
            null);

        await _cards.AddAsync(card, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new Ok<VirtualCardItem>(ToItem(card));
    }

    /// <summary>Freezes a card or lets it be used again.</summary>
    public async Task<Result<VirtualCardItem>> SetFrozenAsync(
        Guid userId,
        Guid cardId,
        bool frozen,
        CancellationToken cancellationToken = default)
    {
        VirtualCard? card = await _cards.GetByIdAsync(cardId, userId, cancellationToken);

        if (card is null)
        {
            return new Fail<VirtualCardItem>("The card does not exist.")
            {
                Code = "card_not_found"
            };
        }

        if (card.Status == VirtualCardStatus.Expired)
        {
            return new Fail<VirtualCardItem>("The card is not valid any more.")
            {
                Code = "card_expired"
            };
        }

        VirtualCard updated = card with
        {
            Status = frozen ? VirtualCardStatus.Frozen : VirtualCardStatus.Active,
            FrozenAtUtc = frozen ? _clock.UtcNow : null
        };

        await _cards.UpdateAsync(updated, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new Ok<VirtualCardItem>(ToItem(updated));
    }

    /// <summary>Changes the limits of a card.</summary>
    public async Task<Result<VirtualCardItem>> SetLimitsAsync(
        Guid userId,
        Guid cardId,
        decimal dailyLimit,
        decimal monthlyLimit,
        CancellationToken cancellationToken = default)
    {
        VirtualCard? card = await _cards.GetByIdAsync(cardId, userId, cancellationToken);

        if (card is null)
        {
            return new Fail<VirtualCardItem>("The card does not exist.")
            {
                Code = "card_not_found"
            };
        }

        VirtualCard updated = card with
        {
            DailyLimit = Money.FromDecimal(dailyLimit < 0m ? 0m : dailyLimit),
            MonthlyLimit = Money.FromDecimal(monthlyLimit < 0m ? 0m : monthlyLimit)
        };

        await _cards.UpdateAsync(updated, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new Ok<VirtualCardItem>(ToItem(updated));
    }

    /// <summary>Builds the read model of a card.</summary>
    public static VirtualCardItem ToItem(VirtualCard card) =>
        new(
            card.Id,
            card.Nickname,
            card.MaskedNumber,
            card.LastFour,
            card.Status,
            card.LinkedAccountId,
            card.DailyLimit,
            card.MonthlyLimit,
            card.CreatedAtUtc);
}
