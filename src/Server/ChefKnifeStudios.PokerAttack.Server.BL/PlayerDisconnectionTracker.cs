using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;

namespace ChefKnifeStudios.PokerAttack.Server.BL;

public interface IPlayerDisconnectionTracker
{
    void TrackDisconnection(string playerId, string gameId);
    void CancelDisconnectionTimer(string playerId);
}

/// <summary>
/// Tracks player disconnections and eliminates them after a timeout period
/// </summary>
public class PlayerDisconnectionTracker : IPlayerDisconnectionTracker, IDisposable
{
    private readonly ILogger<PlayerDisconnectionTracker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _disconnectionTimers = new();

    private const int DISCONNECTION_TIMEOUT_MS = 60000; // 1 minute

    public PlayerDisconnectionTracker(
        ILogger<PlayerDisconnectionTracker> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public void TrackDisconnection(string playerId, string gameId)
    {
        // Cancel any existing timer for this player
        CancelDisconnectionTimer(playerId);

        var cts = new CancellationTokenSource();
        _disconnectionTimers[playerId] = cts;

        _logger.LogInformation("Player {playerId} disconnected from game {gameId}. Starting {timeout}ms elimination timer",
            playerId, gameId, DISCONNECTION_TIMEOUT_MS);

        // Start async timer task
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DISCONNECTION_TIMEOUT_MS, cts.Token);

                // If we reach here, the player didn't reconnect in time
                await EliminateDisconnectedPlayer(playerId, gameId);
            }
            catch (TaskCanceledException)
            {
                // Timer was cancelled, player reconnected
                _logger.LogInformation("Player {playerId} reconnected before timeout", playerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in disconnection timer for player {playerId}", playerId);
            }
            finally
            {
                // Cleanup
                _disconnectionTimers.TryRemove(playerId, out _);
                cts.Dispose();
            }
        }, cts.Token);
    }

    public void CancelDisconnectionTimer(string playerId)
    {
        if (_disconnectionTimers.TryRemove(playerId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _logger.LogInformation("Cancelled disconnection timer for player {playerId}", playerId);
        }
    }

    private async Task EliminateDisconnectedPlayer(string playerId, string gameId)
    {
        try
        {
            _logger.LogWarning("Player {playerId} failed to reconnect within timeout. Eliminating from game {gameId}",
                playerId, gameId);

            // Use a new scope to avoid service lifetime issues
            using var scope = _serviceProvider.CreateScope();
            var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();

            await gameService.EliminateDisconnectedPlayerAsync(playerId, gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error eliminating disconnected player {playerId} from game {gameId}", playerId, gameId);
        }
    }

    public void Dispose()
    {
        foreach (var cts in _disconnectionTimers.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _disconnectionTimers.Clear();
    }
}
