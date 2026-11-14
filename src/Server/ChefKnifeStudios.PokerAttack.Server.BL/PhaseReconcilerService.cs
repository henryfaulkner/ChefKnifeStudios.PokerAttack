using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace ChefKnifeStudios.PokerAttack.Server.BL;

public interface IPhaseReconcilerService
{
    /// <summary>
    /// Advances any phase whose PhaseExpiresAt has passed. Loops per game until nothing is
    /// overdue, so multiple deadlines expired during one interruption resolve to the correct
    /// current phase instead of stopping one transition short.
    /// </summary>
    Task ReconcileAllAsync(CancellationToken ct = default);

    /// <summary>Reconciles a single game. Used on demand (incoming request, SignalR reconnect).</summary>
    Task ReconcileGameAsync(string gameId, CancellationToken ct = default);
}

public class PhaseReconcilerService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<ResilienceSettings> resilienceSettings,
    ILogger<PhaseReconcilerService> logger) : IPhaseReconcilerService
{
    // Single-replica assumption (plan.md: maxReplicas stays 1) — a process-local per-game lock
    // is sufficient to make "only one caller advances a given phase" hold under concurrent calls.
    static readonly ConcurrentDictionary<string, SemaphoreSlim> _gameLocks = new();

    public async Task ReconcileAllAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var activeGameRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<ActiveGame>>();
        var playerSessionStore = scope.ServiceProvider.GetRequiredService<IPlayerSessionStore>();

        var allGamesResult = await activeGameRepository.GetAllAsync(ct);
        if (allGamesResult.IsSuccess)
        {
            foreach (var gameId in allGamesResult.Value.Keys.ToList())
                await ReconcileGameAsync(gameId, ct);
        }

        await ReconcileOverdueSessionsAsync(playerSessionStore, ct);
    }

    public async Task ReconcileGameAsync(string gameId, CancellationToken ct = default)
    {
        var gameLock = _gameLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync(ct);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var activeGameRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<ActiveGame>>();
            var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();

            // Loop until nothing is overdue: an interruption can span several deadlines
            // (e.g. round -> scoreboard -> elimination all expired while the process was down).
            while (true)
            {
                var gameResult = await activeGameRepository.GetAsync(gameId, ct);
                if (!gameResult.IsSuccess || gameResult.Value is not ActiveGame game)
                    return;

                if (game.PhaseExpiresAt is not DateTime expiresAt || expiresAt > clock.UtcNow)
                    return;

                var phase = game.CurrentPhase;
                logger.LogInformation(
                    "Reconciling overdue phase: GameId={GameId}, Phase={Phase}, ExpiresAt={ExpiresAt}",
                    gameId, phase, expiresAt);

                // Clear the deadline before advancing so a phase that turns out to be untimed
                // (Finish* no-ops because the state already moved on) doesn't spin the loop forever.
                game.PhaseExpiresAt = null;
                await activeGameRepository.UpdateAsync(gameId, game, ct);

                switch (phase)
                {
                    case GameStates.InGame:
                        await gameService.EndRoundAsync(gameId, ct);
                        break;
                    case GameStates.Scoreboard:
                        await gameService.FinishScoreboardAsync(gameId, ct);
                        break;
                    case GameStates.Elimination:
                        await gameService.FinishEliminationAsync(gameId, ct);
                        break;
                    case GameStates.Shop:
                        await gameService.FinishShoppingAsync(gameId, ct);
                        break;
                    case GameStates.Freebie:
                        await gameService.FinishPlayerPowerSelectionAsync(gameId, ct);
                        break;
                    default:
                        return;
                }
            }
        }
        finally
        {
            gameLock.Release();
        }
    }

    async Task ReconcileOverdueSessionsAsync(IPlayerSessionStore playerSessionStore, CancellationToken ct)
    {
        var grace = TimeSpan.FromSeconds(resilienceSettings.Value.GracePeriodSeconds);
        var threshold = clock.UtcNow - grace;

        var overdueResult = await playerSessionStore.FindOverdueByDisconnectedAtAsync(threshold, ct);
        if (!overdueResult.IsSuccess)
            return;

        using var scope = scopeFactory.CreateScope();
        var lobbyService = scope.ServiceProvider.GetRequiredService<ILobbyService>();
        var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();

        foreach (var session in overdueResult.Value)
        {
            logger.LogInformation(
                "Grace period expired, removing disconnected player: PlayerId={PlayerId}, GameId={GameId}, LobbyId={LobbyId}",
                session.PlayerId, session.GameId, session.LobbyId);

            if (session.GameId is string gameId)
                await gameService.LeaveGameAsync(gameId, session.PlayerId, ct);

            if (session.LobbyId is string lobbyId)
                await lobbyService.LeaveLobbyAsync(lobbyId, new() { Id = session.PlayerId, Name = session.PlayerId }, ct);

            await playerSessionStore.UpsertAsync(new()
            {
                PlayerId = session.PlayerId,
                GameId = null,
                LobbyId = null,
                Connected = false,
                DisconnectedAt = session.DisconnectedAt,
                UpdatedAt = clock.UtcNow,
            }, ct);
        }
    }
}
