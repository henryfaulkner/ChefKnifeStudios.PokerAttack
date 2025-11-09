using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

// Ensure a singleton subscriber that can resolve scoped services when events arrive
public sealed class GameEventSubscriber : IHostedService
{
    readonly IEventNotificationService _eventBus;
    readonly ILogger<GameEventSubscriber> _logger;
    readonly IServiceScopeFactory _scopeFactory;

    public GameEventSubscriber(
        IEventNotificationService eventBus,
        ILogger<GameEventSubscriber> logger,
        IServiceScopeFactory scopeFactory)
    {
        _eventBus = eventBus;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _eventBus.EventReceived += OnEventReceived;
        _logger.LogInformation("GameEventSubscriber started and subscribed to EventNotificationService.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _eventBus.EventReceived -= OnEventReceived;
        _logger.LogInformation("GameEventSubscriber stopped and unsubscribed.");
        return Task.CompletedTask;
    }

    async Task OnEventReceived(object sender, IEventArgs args)
    {
        try
        {
            if (args is not GameStateChangedEventArgs gs) return;

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
                        await Task.Delay(5000);
                        await gameService.FinishEliminationAsync(gs.GameId);
                        break;
                    }

            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling backend event in GameEventSubscriber.");
        }
    }
}