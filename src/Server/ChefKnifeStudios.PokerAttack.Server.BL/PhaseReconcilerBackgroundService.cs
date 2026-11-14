using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;

namespace ChefKnifeStudios.PokerAttack.Server.BL;

/// <summary>
/// Periodically sweeps overdue phases and disconnect grace periods, so an idle period that
/// stopped the container does not defer reconciliation indefinitely. On-demand reconciliation
/// (incoming gameplay request, SignalR reconnect) happens via direct IPhaseReconcilerService
/// calls elsewhere; this hosted service is the timer-driven fallback plus the startup sweep.
/// </summary>
public class PhaseReconcilerBackgroundService(
    IPhaseReconcilerService reconciler,
    IOptions<ResilienceSettings> resilienceSettings,
    ILogger<PhaseReconcilerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(resilienceSettings.Value.ReconcilerIntervalSeconds);

        // Opportunistic sweep at startup: an interruption may have left overdue phases or
        // grace periods behind while nothing was running to reconcile them.
        await RunOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await reconciler.ReconcileAllAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during phase reconciliation sweep");
        }
    }
}
