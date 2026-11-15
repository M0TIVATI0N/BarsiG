using FinanceFlow.Web;

namespace FinanceFlow.UnitTests;

public sealed class PhoneVerificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Challenge_SuccessIsSingleUseAndCodeIsNotNeededForReplay()
    {
        LocalPhoneVerification service = new();
        Assert.True(service.TryIssue("subject", "+14155552671", "local-session", Now, out LocalPhoneVerification.Issued? challenge));

        Assert.True(service.Verify(challenge!.Id, challenge.DemoCode, Now).Succeeded);
        Assert.False(service.Verify(challenge.Id, challenge.DemoCode, Now).Succeeded);
    }

    [Fact]
    public void Challenge_RejectsIncorrectCodeAndExpires()
    {
        LocalPhoneVerification service = new();
        Assert.True(service.TryIssue("subject", "+14155552671", "session-a", Now, out LocalPhoneVerification.Issued? challenge));
        Assert.False(service.Verify(challenge!.Id, "000000", Now).Succeeded);
        Assert.False(service.Verify(challenge.Id, challenge.DemoCode, Now.AddMinutes(6)).Succeeded);
    }

    [Fact]
    public void Challenge_LimitsAttemptsAndResendCooldown()
    {
        LocalPhoneVerification service = new();
        Assert.True(service.TryIssue("subject", "+14155552671", "session-a", Now, out LocalPhoneVerification.Issued? challenge));
        Assert.False(service.TryIssue(
            "subject",
            "+14155552671",
            "session-a",
            Now.AddSeconds(10),
            out _,
            out TimeSpan retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(50), retryAfter);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            Assert.False(service.Verify(challenge!.Id, "999999", Now.AddSeconds(attempt)).Succeeded);
        }

        Assert.False(service.Verify(challenge!.Id, challenge.DemoCode, Now.AddSeconds(10)).Succeeded);
        Assert.True(service.TryIssue("subject", "+14155552671", "session-a", Now.AddMinutes(2), out _));
    }

    [Fact]
    public void ExpiredChallengeCanBeResumedAndReissuedWithoutAcceptingTheOldCode()
    {
        LocalPhoneVerification service = new();
        Assert.True(service.TryIssue("subject", "+14155552671", "session-a", Now, out LocalPhoneVerification.Issued? oldChallenge));

        Assert.False(service.Verify(oldChallenge!.Id, oldChallenge.DemoCode, Now.AddMinutes(6)).Succeeded);
        Assert.True(service.TryGetBinding(oldChallenge.Id, Now.AddMinutes(6), out LocalPhoneVerification.ChallengeBinding? oldBinding));
        Assert.True(oldBinding!.IsExpired);

        Assert.True(service.TryIssue(
            oldBinding.Subject,
            oldBinding.Phone,
            "session-a",
            Now.AddMinutes(6),
            out LocalPhoneVerification.Issued? newChallenge));
        Assert.False(service.Verify(oldChallenge.Id, oldChallenge.DemoCode, Now.AddMinutes(6)).Succeeded);
        Assert.True(service.Verify(newChallenge!.Id, newChallenge.DemoCode, Now.AddMinutes(6)).Succeeded);
        Assert.False(service.Verify(newChallenge.Id, newChallenge.DemoCode, Now.AddMinutes(6)).Succeeded);
    }

    [Fact]
    public void IssuanceIsCappedAtFiveChallengesPerHourForPhoneAndClient()
    {
        LocalPhoneVerification service = new();

        for (int issue = 0; issue < 5; issue++)
        {
            Assert.True(service.TryIssue(
                "subject",
                "+14155552671",
                "session-a",
                Now.AddSeconds(issue * 61),
                out _));
        }

        Assert.False(service.TryIssue(
            "subject",
            "+14155552671",
            "session-a",
            Now.AddSeconds(5 * 61),
            out _,
            out TimeSpan retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Theory]
    [InlineData("Development", true, true)]
    [InlineData("Development", false, false)]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, false)]
    public void DemoModeRequiresDevelopmentAndExplicitOptIn(
        string environmentName,
        bool explicitlyEnabled,
        bool expected)
    {
        Assert.Equal(
            expected,
            LocalPhoneVerification.IsDemoModeEnabled(environmentName, explicitlyEnabled));
    }
}
