using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

// Ensure a singleton subscriber that can resolve scoped services when events arrive
public sealed class EventNotificationServiceSubscriber : IHostedService
{
    readonly IEventNotificationService _eventBus;
    readonly ILogger<EventNotificationServiceSubscriber> _logger;
    readonly IServiceScopeFactory _scopeFactory;
    readonly GameSettings _gameSettings;
    readonly IClock _clock;

    public EventNotificationServiceSubscriber(
        IEventNotificationService eventBus,
        ILogger<EventNotificationServiceSubscriber> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<GameSettings> gameSettings,
        IClock clock)
    {
        _eventBus = eventBus;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _gameSettings = gameSettings.Value;
        _clock = clock;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _eventBus.EventReceived += OnEventReceived;
        _logger.LogInformation("EventNotificationServiceSubscriber started and subscribed to EventNotificationService.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _eventBus.EventReceived -= OnEventReceived;
        _logger.LogInformation("EventNotificationServiceSubscriber stopped and unsubscribed.");
        return Task.CompletedTask;
    }

    async Task OnEventReceived(object sender, IEventArgs args)
    {
        try
        {
            switch (args)
            {
                case GameStateChangedEventArgs gs:
                    {
                        // create a scope to resolve scoped services (GameService, etc.)
                        using var scope = _scopeFactory.CreateScope();
                        var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();
                        var activeGameRepository = scope.ServiceProvider.GetRequiredService<IKeyValueRepository<ActiveGame>>();

                        _logger.LogInformation("Dispatching GameStateChanged for GameId={GameId} NewState={NewState}", gs.GameId, gs.NewState);

                        switch (gs.NewState)
                        {
                            case GameStates.InGame:
                                {
                                    await gameService.StartRoundAsync(gs.GameId);
                                    break;
                                }
                            case GameStates.Elimination:
                                {
                                    await gameService.StartEliminationAsync(gs.GameId);
                                    // Persisted deadline replaces Task.Delay: the reconciler calls
                                    // FinishEliminationAsync once this expires, so elimination survives
                                    // an interruption instead of wedging.
                                    await SetPhaseDeadlineAsync(activeGameRepository, gs.GameId, GameStates.Elimination, _gameSettings.EliminationTimeMs);
                                    break;
                                }
                            case GameStates.Shop:
                                {
                                    await gameService.StartShoppingAsync(gs.GameId);
                                    await SetPhaseDeadlineAsync(activeGameRepository, gs.GameId, GameStates.Shop, _gameSettings.ShopTimeMs);
                                    break;
                                }
                            case GameStates.Freebie:
                                {
                                    _logger.LogInformation("Freebie timer starting for GameId={GameId}", gs.GameId);
                                    await gameService.StartPlayerPowerSelectionAsync(gs.GameId);
                                    await SetPhaseDeadlineAsync(activeGameRepository, gs.GameId, GameStates.Freebie, _gameSettings.PlayerPowerSelectionTimeMs);
                                    break;
                                }
                            case GameStates.GameOver:
                                {
                                    _logger.LogInformation("Game over - cleaning up GameId={GameId}", gs.GameId);
                                    await gameService.EndGameAsync(gs.GameId);
                                    break;
                                }

                        }
                        break;
                    }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling backend event in EventNotificationServiceSubscriber.");
        }
    }

    async Task SetPhaseDeadlineAsync(IKeyValueRepository<ActiveGame> activeGameRepository, string gameId, GameStates phase, int durationMs)
    {
        var activeGameResult = await activeGameRepository.GetAsync(gameId);
        if (!activeGameResult.IsSuccess || activeGameResult.Value is not ActiveGame activeGame)
            return;

        var now = _clock.UtcNow;
        activeGame.CurrentPhase = phase;
        activeGame.PhaseStartedAt = now;
        activeGame.PhaseExpiresAt = now.AddMilliseconds(durationMs);

        await activeGameRepository.UpdateAsync(gameId, activeGame);
    }
}