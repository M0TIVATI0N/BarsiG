using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Banking;
using FinanceFlow.Application.Banking.Dtos;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceFlow.IntegrationTests;

public sealed class PhoneTransferIntegrationTests
{
    private const string RecipientPhone = "+77001234567";

    [Fact]
    public async Task TransferService_FindsVerifiedRecipientAndMovesFundsThroughTheLedger()
    {
        using BankingFixture fixture = new(allowLocalDemoPhoneTransfers: true);
        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.test", "Sender");
        BankAccount recipient = await fixture.CreateVerifiedPhoneCustomerAsync(
            "recipient@example.test",
            "Recipient Customer",
            RecipientPhone);
        await fixture.FundAsync(sender, 2_000m);

        await fixture.RunAsync(async provider =>
        {
            TransferService service = provider.GetRequiredService<TransferService>();
            Result<PhoneRecipientPreview> found = await service.FindRecipientByPhoneAsync(
                sender.UserId!.Value,
                "+7 (700) 123-4567");

            Assert.True(found.IsSuccess());
            PhoneRecipientPreview recipientPreview = found.ValueOr(default!);
            Assert.Equal(recipient.UserId, recipientPreview.UserId);
            Assert.Equal("Recipient Customer", recipientPreview.DisplayName);
            Assert.Contains("4567", recipientPreview.MaskedPhoneNumber);
            Assert.DoesNotContain(RecipientPhone, recipientPreview.MaskedPhoneNumber);
            Assert.Contains("Account ending in", recipientPreview.DestinationAccountHint);

            TransferRequest request = new(
                recipient.UserId!.Value,
                325m,
                "Shared project supplies",
                Guid.NewGuid().ToString(),
                RecipientPhoneNumber: RecipientPhone);

            Result<TransferPreview> preview = await service.PreviewAsync(sender.UserId.Value, request);

            Assert.True(preview.IsSuccess());
            Assert.Equal(recipientPreview.MaskedPhoneNumber, preview.ValueOr(default!).RecipientPhoneHint);
            Assert.Empty(preview.ValueOr(default!).RecipientEmailHint);
            Assert.Contains("Account ending in", preview.ValueOr(default!).RecipientAccountNumber);

            Result<TransferOutcome> completed = await service.ExecuteAsync(sender.UserId.Value, request);
            Assert.True(completed.IsSuccess());
        });

        Assert.Equal(1_675_00, (await fixture.ReadBalanceAsync(sender.Id)).Cents);
        Assert.Equal(325_00, (await fixture.ReadBalanceAsync(recipient.Id)).Cents);

        await fixture.RunAsync(async provider =>
        {
            ILedgerRepository ledger = provider.GetRequiredService<ILedgerRepository>();
            Assert.Equal(1_675_00, (await ledger.ComputeBalanceAsync(sender.Id)).Cents);
            Assert.Equal(325_00, (await ledger.ComputeBalanceAsync(recipient.Id)).Cents);

            LedgerEntry[] senderEntries = (await ledger.GetEntriesOfAccountAsync(sender.Id, 20)).ToArray();
            LedgerEntry[] recipientEntries = (await ledger.GetEntriesOfAccountAsync(recipient.Id, 20)).ToArray();
            Assert.Contains(senderEntries, entry => entry.Direction == LedgerEntryDirection.Debit && entry.Amount.Cents == 32_500);
            Assert.Contains(recipientEntries, entry => entry.Direction == LedgerEntryDirection.Credit && entry.Amount.Cents == 32_500);
        });
    }

    [Fact]
    public async Task PreviewAsync_RejectsARecipientIdThatDoesNotMatchTheVerifiedPhone()
    {
        using BankingFixture fixture = new(allowLocalDemoPhoneTransfers: true);
        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.test", "Sender");
        _ = await fixture.CreateVerifiedPhoneCustomerAsync(
            "recipient@example.test",
            "Phone Owner",
            RecipientPhone);
        BankAccount other = await fixture.CreateCustomerAsync("other@example.test", "Different Customer");

        await fixture.RunAsync(async provider =>
        {
            TransferService service = provider.GetRequiredService<TransferService>();
            TransferRequest forged = new(
                other.UserId!.Value,
                100m,
                null,
                Guid.NewGuid().ToString(),
                RecipientPhoneNumber: RecipientPhone);

            Result<TransferPreview> result = await service.PreviewAsync(sender.UserId!.Value, forged);

            Assert.True(result.IsFailure());
            Assert.Equal("recipient_not_found", ((Fail<TransferPreview>)result).Code);
        });
    }

    [Fact]
    public async Task PreviewAsync_RejectsAccountsWithIncompatibleCurrencies()
    {
        using BankingFixture fixture = new(allowLocalDemoPhoneTransfers: true);
        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.test", "Sender");
        BankAccount recipient = await fixture.CreateVerifiedPhoneCustomerAsync(
            "recipient@example.test",
            "Recipient",
            RecipientPhone);

        await fixture.RunAsync(async provider =>
        {
            FinanceFlowDbContext db = provider.GetRequiredService<FinanceFlowDbContext>();
            BankAccount trackedRecipient = await db.BankAccounts.SingleAsync(account => account.Id == recipient.Id);
            db.Entry(trackedRecipient).CurrentValues.SetValues(trackedRecipient with { Currency = "USD" });
            await db.SaveChangesAsync();

            TransferRequest request = new(
                recipient.UserId!.Value,
                100m,
                "Currency compatibility",
                Guid.NewGuid().ToString(),
                RecipientPhoneNumber: RecipientPhone);

            Result<TransferPreview> result = await provider.GetRequiredService<TransferService>()
                .PreviewAsync(sender.UserId!.Value, request);

            Assert.True(result.IsFailure());
            Assert.Equal("currency_mismatch", ((Fail<TransferPreview>)result).Code);
        });
    }

    [Fact]
    public async Task PhoneLookup_RejectsLocalDemoIdentityWhenDevelopmentOptInIsOff()
    {
        using BankingFixture fixture = new();
        BankAccount recipient = await fixture.CreateVerifiedPhoneCustomerAsync(
            "recipient@example.test",
            "Demo Verified Recipient",
            RecipientPhone);

        await fixture.RunAsync(async provider =>
        {
            TransferService service = provider.GetRequiredService<TransferService>();

            Result<PhoneRecipientPreview> result =
                await service.FindRecipientByPhoneAsync(Guid.NewGuid(), RecipientPhone);

            Assert.True(result.IsFailure());
            Assert.Equal("recipient_not_found", ((Fail<PhoneRecipientPreview>)result).Code);
            Assert.NotEqual(Guid.Empty, recipient.UserId);
        });
    }

    [Fact]
    public async Task LedgerExecutor_RejectsPhoneOwnershipChangedAfterRecipientReviewWithoutDebit()
    {
        using BankingFixture fixture = new(allowLocalDemoPhoneTransfers: true);
        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.test", "Sender");
        BankAccount recipient = await fixture.CreateVerifiedPhoneCustomerAsync(
            "recipient@example.test",
            "Recipient",
            RecipientPhone);
        await fixture.FundAsync(sender, 1_000m);

        await fixture.RunAsync(async provider =>
        {
            FinanceFlowDbContext db = provider.GetRequiredService<FinanceFlowDbContext>();
            FinanceFlow.Domain.Entities.User user = await db.Users.SingleAsync(
                candidate => candidate.Id == recipient.UserId);
            db.Entry(user).CurrentValues.SetValues(user with
            {
                PhoneNumber = "+77007654321",
                IsPhoneVerified = true,
                PhoneVerificationMethod = PhoneVerificationMethod.LocalDemo
            });
            await db.SaveChangesAsync();
        });

        Result<LedgerTransfer> result = await fixture.RunAsync(provider =>
            provider.GetRequiredService<ILedgerExecutor>().ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                recipient.Id,
                sender.UserId!.Value,
                Money.FromDecimal(100m),
                "Phone ownership changed",
                Guid.NewGuid().ToString(),
                RecipientPhone)));

        Assert.True(result.IsFailure());
        Assert.Equal("recipient_phone_changed", ((Fail<LedgerTransfer>)result).Code);
        Assert.Equal(100_000, (await fixture.ReadBalanceAsync(sender.Id)).Cents);
        Assert.True((await fixture.ReadBalanceAsync(recipient.Id)).IsZero);
    }
}
