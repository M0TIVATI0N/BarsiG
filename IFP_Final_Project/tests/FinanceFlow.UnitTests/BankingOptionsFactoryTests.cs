using FinanceFlow.Infrastructure.Banking;

using Microsoft.Extensions.Configuration;

namespace FinanceFlow.UnitTests;

public sealed class BankingOptionsFactoryTests
{
    [Theory]
    [InlineData("Development", true, true)]
    [InlineData("Development", false, false)]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, false)]
    public void Create_EnablesLocalDemoRecipientsOnlyWithDevelopmentAndExplicitOptIn(
        string environment,
        bool explicitOptIn,
        bool expected)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runtime:Environment"] = environment,
                ["PhoneVerification:EnableLocalDemo"] = explicitOptIn.ToString()
            })
            .Build();

        Assert.Equal(expected, BankingOptionsFactory.Create(configuration).AllowLocalDemoPhoneRecipients);
    }
}
