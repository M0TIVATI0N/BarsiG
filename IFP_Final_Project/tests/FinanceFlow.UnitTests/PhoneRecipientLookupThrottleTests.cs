using FinanceFlow.Application.Banking;

namespace FinanceFlow.UnitTests;

public sealed class PhoneRecipientLookupThrottleTests
{
    [Fact]
    public void TryAcquire_LimitsEachCustomerToEightLookupsPerMinute()
    {
        PhoneRecipientLookupThrottle throttle = new();
        Guid customer = Guid.NewGuid();
        DateTimeOffset now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Assert.True(throttle.TryAcquire(customer, now));
        }

        Assert.False(throttle.TryAcquire(customer, now));
        Assert.True(throttle.TryAcquire(Guid.NewGuid(), now));
    }

    [Fact]
    public void TryAcquire_AllowsAnotherLookupAfterTheSlidingWindowExpires()
    {
        PhoneRecipientLookupThrottle throttle = new();
        Guid customer = Guid.NewGuid();
        DateTimeOffset now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Assert.True(throttle.TryAcquire(customer, now));
        }

        Assert.True(throttle.TryAcquire(customer, now.AddMinutes(1).AddTicks(1)));
    }
}
