using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using MzansiMarket.Api.Services;

namespace MzansiMarket.Api.Endpoints;

public static class MarketplaceSyncEndpoints
{
    public static IEndpointRouteBuilder MapMarketplaceSyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/sync/stream", StreamAsync).AllowAnonymous().RequireRateLimiting("sync").ExcludeFromDescription();
        return endpoints;
    }

    private static async Task StreamAsync(HttpContext http, MarketplaceChangeFeed feed, CancellationToken cancellationToken)
    {
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache, no-store";
        http.Response.Headers.Append("X-Accel-Buffering", "no");
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await http.Response.StartAsync(cancellationToken);
        var proxyFlushPadding = new string(' ', 16 * 1024);
        await http.Response.WriteAsync($": connected {proxyFlushPadding}\nretry: 3000\n\n", cancellationToken);
        await http.Response.Body.FlushAsync(cancellationToken);

        var subscription = feed.Subscribe();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var available = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
                var completed = await Task.WhenAny(available, Task.Delay(TimeSpan.FromSeconds(20), cancellationToken));
                if (completed == available && await available)
                {
                    while (subscription.Reader.TryRead(out var change))
                    {
                        await http.Response.WriteAsync($"event: sync\ndata: {JsonSerializer.Serialize(change)}\n\n", cancellationToken);
                    }
                }
                else
                {
                    await http.Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                }
                await http.Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            feed.Unsubscribe(subscription.Id);
        }
    }
}
