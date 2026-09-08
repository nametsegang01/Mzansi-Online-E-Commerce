namespace MzansiMarket.Api.Services;

public sealed record MarketplaceChange(long Version, IReadOnlyCollection<string> Scopes, DateTimeOffset OccurredAt);

public sealed class MarketplaceChangeFeed
{
    private readonly object gate = new();
    private MarketplaceChange current = new(1, ["catalogue", "seller", "resellers"], DateTimeOffset.UtcNow);
    private TaskCompletionSource<MarketplaceChange> next = NewSignal();

    public MarketplaceChange Current
    {
        get
        {
            lock (gate) return current;
        }
    }

    public void Publish(params string[] scopes)
    {
        var normalized = scopes.Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Length == 0) return;

        MarketplaceChange change;
        TaskCompletionSource<MarketplaceChange> completed;
        lock (gate)
        {
            change = current = new MarketplaceChange(current.Version + 1, normalized, DateTimeOffset.UtcNow);
            completed = next;
            next = NewSignal();
        }

        completed.TrySetResult(change);
    }

    public async Task<MarketplaceChange> WaitForChangeAsync(
        long afterVersion,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Task<MarketplaceChange> pending;
        lock (gate)
        {
            if (current.Version > afterVersion) return current;
            pending = next.Task;
        }

        try
        {
            return await pending.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            var snapshot = Current;
            return new MarketplaceChange(snapshot.Version, [], snapshot.OccurredAt);
        }
    }

    private static TaskCompletionSource<MarketplaceChange> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
