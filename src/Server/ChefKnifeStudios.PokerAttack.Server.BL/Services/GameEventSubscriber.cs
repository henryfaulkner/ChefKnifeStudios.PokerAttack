using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

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

            if (gs.NewState == GameStates.InGame)
            {
                // call scoped logic
                await gameService.StartRoundAsync(gs.GameId);
            }

            // Add more state reactions as required...
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling backend event in GameEventSubscriber.");
        }
    }
}