using FinanceFlow.Domain.Common;

namespace FinanceFlow.UnitTests;

/// <summary>
/// Tests of the one time password of the authenticator.
/// The test vectors are the ones of RFC 6238, so the implementation is proved against
/// the standard and not against itself.
/// </summary>
public sealed class TotpTests
{
    /// <summary>The secret of the test vectors of the standard: "12345678901234567890".</summary>
    private const string Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Theory]
    [InlineData(59, "94287082")]
    [InlineData(1111111109, "07081804")]
    [InlineData(1111111111, "14050471")]
    [InlineData(1234567890, "89005924")]
    [InlineData(2000000000, "69279037")]
    [InlineData(20000000000, "65353130")]
    public void GenerateCode_MatchesTheTestVectorsOfRfc6238(long timestamp, string expected)
    {
        DateTimeOffset moment = DateTimeOffset.FromUnixTimeSeconds(timestamp);

        string code = Totp.GenerateCode(Secret, moment, stepSeconds: 30, digits: 8);

        Assert.Equal(expected, code);
    }

    [Fact]
    public void GenerateCode_ProducesSixDigitsByDefault()
    {
        string code = Totp.GenerateCode(Secret, DateTimeOffset.UtcNow);

        Assert.Equal(6, code.Length);
        Assert.All(code, character => Assert.True(char.IsDigit(character)));
    }

    [Fact]
    public void VerifyCode_AcceptsTheCodeOfTheCurrentMoment()
    {
        DateTimeOffset moment = new(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);

        string code = Totp.GenerateCode(Secret, moment);

        Assert.True(Totp.VerifyCode(Secret, code, moment));
    }

    [Fact]
    public void VerifyCode_RejectsAWrongCode()
    {
        DateTimeOffset moment = new(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);

        Assert.False(Totp.VerifyCode(Secret, "000000", moment));
    }

    [Fact]
    public void VerifyCode_RejectsACodeOfAnOldMoment()
    {
        DateTimeOffset moment = new(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        string code = Totp.GenerateCode(Secret, moment);

        Assert.False(Totp.VerifyCode(Secret, code, moment.AddMinutes(10)));
    }

    [Fact]
    public void GenerateSecret_CreatesASecretOfTwentyCharacters()
    {
        string secret = Totp.GenerateSecret();

        Assert.Equal(20, secret.Length);
        Assert.All(secret, character => Assert.Contains(character, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
    }

    [Fact]
    public void FormatSecret_GroupsTheSecretInBlocksOfFour()
    {
        string formatted = Totp.FormatSecret("ABCDEFGHIJKLMNOP");

        Assert.Equal("ABCD EFGH IJKL MNOP", formatted);
    }

    [Fact]
    public void FromBase32_ConvertsTheSecretOfTheStandard()
    {
        byte[] key = Totp.FromBase32(Secret);

        Assert.Equal("12345678901234567890", System.Text.Encoding.ASCII.GetString(key));
    }
}
