using MzansiMarket.Api.Services;

namespace MzansiMarket.Api.Tests;

public sealed class MarketplaceChangeFeedTests
{
    [Fact]
    public async Task Publish_CompletesWaitingRequestWithoutDuplicatingScopes()
    {
        var feed = new MarketplaceChangeFeed();
        var initial = feed.Current;
        var pending = feed.WaitForChangeAsync(initial.Version, TimeSpan.FromSeconds(2), CancellationToken.None);

        feed.Publish("catalogue", "seller", "catalogue");
        var change = await pending;

        Assert.True(change.Version > initial.Version);
        Assert.Equal(2, change.Scopes.Count);
        Assert.Contains("catalogue", change.Scopes);
        Assert.Contains("seller", change.Scopes);
    }
}
