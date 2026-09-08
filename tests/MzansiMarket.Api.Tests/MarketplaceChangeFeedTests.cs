using MzansiMarket.Api.Services;

namespace MzansiMarket.Api.Tests;

public sealed class MarketplaceChangeFeedTests
{
    [Fact]
    public async Task Publish_NotifiesActiveSubscribersWithoutDuplicatingScopes()
    {
        var feed = new MarketplaceChangeFeed();
        var subscription = feed.Subscribe();

        feed.Publish("catalogue", "seller", "catalogue");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var change = await subscription.Reader.ReadAsync(timeout.Token);

        Assert.Equal(2, change.Scopes.Count);
        Assert.Contains("catalogue", change.Scopes);
        Assert.Contains("seller", change.Scopes);
        feed.Unsubscribe(subscription.Id);
    }
}
