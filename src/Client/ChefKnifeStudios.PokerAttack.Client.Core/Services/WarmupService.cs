using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services;

public interface IWarmupService
{
    /// <summary>
    /// Fires a debounced, fire-and-forget GET /alive on evident intent to play (opening the
    /// lobby screen, hovering Play). Never blocks the UI and never retries on failure —
    /// explicitly not a keep-warm pinger, which would recreate the idle cost this feature
    /// removes. Call as often as intent occurs; the debounce keeps repeated calls (e.g. a
    /// hover firing many times) from producing a request burst.
    /// </summary>
    void WarmOnIntent();
}

public class WarmupService : IWarmupService
{
    static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(10);

    readonly Func<HttpClient> _httpClientFactory;
    readonly ILogger<WarmupService> _logger;
    DateTime _lastFiredUtc = DateTime.MinValue;
    readonly object _lock = new();

    public WarmupService(Func<HttpClient> httpClientFactory, ILogger<WarmupService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public void WarmOnIntent()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (now - _lastFiredUtc < DebounceWindow)
                return;
            _lastFiredUtc = now;
        }

        _ = FireAndForgetAsync();
    }

    async Task FireAndForgetAsync()
    {
        try
        {
            var client = _httpClientFactory();
            await client.GetAsync("/alive");
        }
        catch (Exception ex)
        {
            // Never surface to the UI — this is a best-effort warm-up, not a user-facing call.
            _logger.LogDebug(ex, "Warm-on-intent request failed (non-fatal)");
        }
    }
}
