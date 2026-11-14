using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Server.BL;

public class CleanupBackgroundService : BackgroundService
{
    readonly ILogger<CleanupBackgroundService> _logger;
    readonly IServiceProvider _serviceProvider;
    readonly IClock _clock;
    readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1); // Run every hour

    public CleanupBackgroundService(
        ILogger<CleanupBackgroundService> logger,
        IServiceProvider serviceProvider,
        IClock clock)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cleanup Background Service started");

        // Opportunistic sweep at startup: an idle period that stopped the container is
        // exactly when cleanup is due and exactly when nothing was running to do it (FR-013).
        await RunSweepAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);
                await RunSweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Cleanup Background Service stopped");
    }

    async Task RunSweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SweepExpiredStateAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during cleanup");
        }
    }

    async Task SweepExpiredStateAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var sweeper = scope.ServiceProvider.GetRequiredService<IExpiredStateSweeper>();
        var lobbyRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<Lobby>>();
        var activeGameRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<ActiveGame>>();
        var gamePlayerRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<GamePlayer>>();
        var gameStateRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<GameStates>>();
        var gameModeRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<GameModes>>();

        var now = _clock.UtcNow;

        // Read the associated GameId before the sweep deletes the Lobby row out from under us.
        var expiringGameIds = new Dictionary<string, string?>();
        var allLobbiesResult = await lobbyRepository.GetAllAsync(cancellationToken);
        if (allLobbiesResult.IsSuccess)
        {
            foreach (var (lobbyId, lobby) in allLobbiesResult.Value)
                expiringGameIds[lobbyId] = lobby.GameId;
        }

        var removed = await sweeper.SweepExpiredAsync(now, cancellationToken);
        if (removed.Count == 0)
        {
            _logger.LogInformation("No expired state found");
            return;
        }

        _logger.LogInformation("Swept {Count} expired state_kv rows", removed.Count);

        // An expired Lobby cascades to its associated ActiveGame's dependent data, mirroring
        // the previous per-lobby cleanup — orphaned GamePlayer/GameStates/GameModes rows would
        // otherwise outlive the lobby that created them.
        foreach (var (type, key) in removed)
        {
            if (type != nameof(Lobby)) continue;
            if (!expiringGameIds.TryGetValue(key, out var gameId) || string.IsNullOrEmpty(gameId)) continue;

            try
            {
                await CleanupGameDataAsync(gameId, activeGameRepository, gamePlayerRepository, gameStateRepository, gameModeRepository, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cascade-cleanup game data for expired lobby {LobbyId}", key);
            }
        }
    }

    async Task CleanupGameDataAsync(
        string gameId,
        IKeyValueRepository<ActiveGame> activeGameRepository,
        IKeyValueRepository<GamePlayer> gamePlayerRepository,
        IKeyValueRepository<GameStates> gameStateRepository,
        IKeyValueRepository<GameModes> gameModeRepository,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get the active game to find all associated players
            var activeGameResult = await activeGameRepository.GetAsync(gameId, cancellationToken);

            if (activeGameResult.IsSuccess && activeGameResult.Value is not null)
            {
                var activeGame = activeGameResult.Value;
                // Delete all GamePlayer entries for this game
                foreach (var player in activeGame.Players)
                {
                    try
                    {
                        await gamePlayerRepository.DeleteAsync(player.Id, cancellationToken);
                        _logger.LogDebug("Deleted GamePlayer {PlayerId} for game {GameId}", player.Id, gameId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete GamePlayer {PlayerId} for game {GameId}", player.Id, gameId);
                    }
                }

                // Delete the ActiveGame
                await activeGameRepository.DeleteAsync(gameId, cancellationToken);
                _logger.LogInformation("Deleted ActiveGame {GameId}", gameId);
            }

            // Delete GameStates entry
            await gameStateRepository.DeleteAsync(gameId, cancellationToken);
            _logger.LogDebug("Deleted GameStates for game {GameId}", gameId);

            // Delete GameModes entry
            await gameModeRepository.DeleteAsync(gameId, cancellationToken);
            _logger.LogDebug("Deleted GameModes for game {GameId}", gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup game data for game {GameId}", gameId);
        }
    }
}
