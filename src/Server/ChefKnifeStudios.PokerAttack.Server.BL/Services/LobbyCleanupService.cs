using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public class LobbyCleanupService : BackgroundService
{
    readonly ILogger<LobbyCleanupService> _logger;
    readonly IServiceProvider _serviceProvider;
    readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1); // Run every hour
    readonly TimeSpan _lobbyMaxAge = TimeSpan.FromHours(24); // Delete lobbies older than 24 hours

    public LobbyCleanupService(
        ILogger<LobbyCleanupService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Lobby Cleanup Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupStaleLobbiesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during lobby cleanup");
            }

            // Wait for next cleanup cycle
            await Task.Delay(_cleanupInterval, stoppingToken);
        }

        _logger.LogInformation("Lobby Cleanup Service stopped");
    }

    async Task CleanupStaleLobbiesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var lobbyRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<Lobby>>();

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
                await lobbyRepository.DeleteAsync(lobbyId, cancellationToken);
                _logger.LogInformation(
                    "Deleted stale lobby {LobbyId} (created {CreatedAt}, age: {Age:F1} hours)",
                    lobbyId,
                    lobby.CreatedAt,
                    (now - lobby.CreatedAt).TotalHours
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete lobby {LobbyId}", lobbyId);
            }
        }

        _logger.LogInformation("Lobby cleanup completed. Deleted {Count} lobbies", staleLobbies.Count);
    }
}
