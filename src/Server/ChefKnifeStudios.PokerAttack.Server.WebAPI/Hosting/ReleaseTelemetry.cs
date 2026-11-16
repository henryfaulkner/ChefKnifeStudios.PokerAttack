using Sentry;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.Hosting;

public static class ReleaseTelemetry
{
    // HttpOnly protects page JS, not server instrumentation. Strip credentials
    // from the existing error/transaction exporter before either can leave host.
    public static void Sanitize(SentryRequest? request)
    {
        if (request is null) return;
        request.Cookies = null;
        foreach (var key in request.Headers.Keys.Where(key =>
            key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)).ToArray())
            request.Headers.Remove(key);
    }
}
