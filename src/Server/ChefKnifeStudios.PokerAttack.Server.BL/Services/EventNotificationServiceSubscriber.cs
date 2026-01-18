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

    public EventNotificationServiceSubscriber(
        IEventNotificationService eventBus,
        ILogger<EventNotificationServiceSubscriber> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<GameSettings> gameSettings)
    {
        _eventBus = eventBus;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _gameSettings = gameSettings.Value;
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
                                    await Task.Delay(_gameSettings.EliminationTimeMs);
                                    await gameService.FinishEliminationAsync(gs.GameId);
                                    break;
                                }
                            case GameStates.Shop:
                                {
                                    await gameService.StartShoppingAsync(gs.GameId);
                                    await Task.Delay(_gameSettings.ShopTimeMs);
                                    await gameService.FinishShoppingAsync(gs.GameId);
                                    break;
                                }
                            case GameStates.Freebie:
                                {
                                    _logger.LogInformation("Freebie timer starting for GameId={GameId}", gs.GameId);
                                    await gameService.StartPlayerPowerSelectionAsync(gs.GameId);
                                    await Task.Delay(_gameSettings.PlayerPowerSelectionTimeMs);
                                    _logger.LogInformation("Freebie timer expired for GameId={GameId}, calling FinishPlayerPowerSelectionAsync", gs.GameId);
                                    await gameService.FinishPlayerPowerSelectionAsync(gs.GameId);
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
}