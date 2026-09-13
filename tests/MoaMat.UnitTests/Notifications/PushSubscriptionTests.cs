using MoaMat.Domain.Notifications;

namespace MoaMat.UnitTests.Notifications;

/// <summary>
/// A subscription the database would refuse is caught before the round-trip.
/// </summary>
public sealed class PushSubscriptionTests
{
    private const string Endpoint = "https://fcm.googleapis.com/fcm/send/abc123";

    [Fact]
    public void A_complete_subscription_is_accepted()
    {
        Assert.True(new PushSubscription(Endpoint, "p256dh-key", "auth-secret").IsComplete);
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc123")]
    [InlineData("/relative/endpoint")]
    [InlineData("")]
    public void An_endpoint_that_is_not_absolute_https_is_refused(string endpoint)
    {
        Assert.False(new PushSubscription(endpoint, "p256dh-key", "auth-secret").IsComplete);
    }

    [Theory]
    [InlineData("", "auth-secret")]
    [InlineData("p256dh-key", " ")]
    public void A_missing_key_is_refused(string p256dh, string auth)
    {
        Assert.False(new PushSubscription(Endpoint, p256dh, auth).IsComplete);
    }
}
