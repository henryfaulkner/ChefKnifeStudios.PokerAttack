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
    readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1); // Run every hour
    readonly TimeSpan _lobbyMaxAge = TimeSpan.FromHours(24); // Delete lobbies older than 24 hours

    public CleanupBackgroundService(
        ILogger<CleanupBackgroundService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cleanup Background Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupStaleLobbiesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during cleanup");
            }

            // Wait for next cleanup cycle
            await Task.Delay(_cleanupInterval, stoppingToken);
        }

        _logger.LogInformation("Cleanup Background Service stopped");
    }

    async Task CleanupStaleLobbiesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var lobbyRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<Lobby>>();
        var activeGameRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<ActiveGame>>();
        var gamePlayerRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<GamePlayer>>();
        var gameStateRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<GameStates>>();
        var gameModeRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<GameModes>>();

        var allLobbies = await lobbyRepository.GetAllAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var staleLobbies = allLobbies
            .Where(kvp => (now - kvp.Value.CreatedAt) > _lobbyMaxAge)
            .ToList();

        if (!staleLobbies.Any())
        {
            _logger.LogInformation("No stale lobbies found");
            return;
        }

        _logger.LogInformation("Found {Count} stale lobbies to cleanup", staleLobbies.Count);

        foreach (var (lobbyId, lobby) in staleLobbies)
        {
            try
            {
                // Delete the lobby first
                await lobbyRepository.DeleteAsync(lobbyId, cancellationToken);
                _logger.LogInformation(
                    "Deleted stale lobby {LobbyId} (created {CreatedAt}, age: {Age:F1} hours)",
                    lobbyId,
                    lobby.CreatedAt,
                    (now - lobby.CreatedAt).TotalHours
                );

                // If the lobby has an associated game, cleanup all related game data
                if (!string.IsNullOrEmpty(lobby.GameId))
                {
                    await CleanupGameDataAsync(
                        lobby.GameId,
                        activeGameRepository,
                        gamePlayerRepository,
                        gameStateRepository,
                        gameModeRepository,
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete lobby {LobbyId}", lobbyId);
            }
        }

        _logger.LogInformation("Lobby cleanup completed. Deleted {Count} lobbies", staleLobbies.Count);
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
            var activeGame = await activeGameRepository.GetAsync(gameId, cancellationToken);

            if (activeGame != null)
            {
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
