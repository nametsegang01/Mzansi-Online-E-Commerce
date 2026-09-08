using MzansiMarket.Api.Services;

namespace MzansiMarket.Api.Endpoints;

public static class MarketplaceSyncEndpoints
{
    public static IEndpointRouteBuilder MapMarketplaceSyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/sync/changes", WaitForChangesAsync).AllowAnonymous().RequireRateLimiting("sync").ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<IResult> WaitForChangesAsync(
        long after,
        MarketplaceChangeFeed feed,
        CancellationToken cancellationToken)
    {
        var change = await feed.WaitForChangeAsync(
            Math.Max(0, after),
            TimeSpan.FromSeconds(20),
            cancellationToken);
        return Results.Ok(change);
    }
}
