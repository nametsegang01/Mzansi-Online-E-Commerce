using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MzansiMarket.Api.Services;

public sealed record MarketplaceChange(IReadOnlyCollection<string> Scopes, DateTimeOffset OccurredAt);

public sealed class MarketplaceChangeFeed
{
    private readonly ConcurrentDictionary<Guid, Channel<MarketplaceChange>> subscribers = new();

    public (Guid Id, ChannelReader<MarketplaceChange> Reader) Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<MarketplaceChange>(new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (subscribers.TryRemove(id, out var channel)) channel.Writer.TryComplete();
    }

    public void Publish(params string[] scopes)
    {
        var normalized = scopes.Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Length == 0) return;
        var change = new MarketplaceChange(normalized, DateTimeOffset.UtcNow);
        foreach (var channel in subscribers.Values) channel.Writer.TryWrite(change);
    }
}
