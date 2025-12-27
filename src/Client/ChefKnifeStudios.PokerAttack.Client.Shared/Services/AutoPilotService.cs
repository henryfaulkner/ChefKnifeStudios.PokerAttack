using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.Agents;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Agent;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using ChefKnifeStudios.PokerAttack.Shared.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface IAutoPilotService : IDisposable
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();
}

public class AutoPilotService : IAutoPilotService
{
    private readonly IAgentBrain _brain;
    private readonly ISignalRNotificationService _signalR;
    private readonly IGameplayViewModel _gameplayVM;
    private readonly IGameStateMachineViewModel _gameStateMachineVM;
    private readonly IGameDataStore _gameDataStore;
    private readonly IApplicationViewModel _applicationVM;
    private readonly IPlayerPowerEndpointsService _playerPowerEndpoints;
    private readonly AgentSettings _settings;
    private readonly ILogger<AutoPilotService> _logger;

    private CancellationTokenSource? _roundCancellation;
    private bool _isEnabled;

    public bool IsEnabled => _isEnabled;

    public AutoPilotService(
        IAgentBrain brain,
        ISignalRNotificationService signalR,
        IGameplayViewModel gameplayVM,
        IGameStateMachineViewModel gameStateMachineVM,
        IGameDataStore gameDataStore,
        IApplicationViewModel applicationVM,
        IPlayerPowerEndpointsService playerPowerEndpoints,
        AgentSettings settings,
        ILogger<AutoPilotService> logger)
    {
        _brain = brain;
        _signalR = signalR;
        _gameplayVM = gameplayVM;
        _gameStateMachineVM = gameStateMachineVM;
        _gameDataStore = gameDataStore;
        _applicationVM = applicationVM;
        _playerPowerEndpoints = playerPowerEndpoints;
        _settings = settings;
        _logger = logger;

        _signalR.HandleNotificationReceived += OnNotificationReceived;
    }

    public void Enable()
    {
        _isEnabled = true;
        _logger.LogInformation("[AutoPilot] Enabled for player {PlayerId}", _applicationVM.Player.Id);
    }

    public void Disable()
    {
        _isEnabled = false;
        StopInGameLoop();
        _logger.LogInformation("[AutoPilot] Disabled");
    }

    private async Task OnNotificationReceived(PokerAttackNotification notification)
    {
        if (!_isEnabled) return;

        try
        {
            switch (notification.NotificationType)
            {
                case PokerAttackNotificationType.RunStarted:
                    _logger.LogInformation("[AutoPilot] Run started, beginning action loop");
                    await StartInGameLoop();
                    break;

                case PokerAttackNotificationType.RoundEnded:
                    _logger.LogInformation("[AutoPilot] Round ended, stopping action loop");
                    StopInGameLoop();
                    break;

                case PokerAttackNotificationType.GameStateChanged:
                    var newState = JsonSerializer.Deserialize<GameStates>(notification.Payload!, JsonOptions.Get());
                    _logger.LogInformation("[AutoPilot] Game state changed to {State}", newState);
                    await HandleStateChange(newState);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoPilot] Error handling notification {Type}", notification.NotificationType);
        }
    }

    private async Task StartInGameLoop()
    {
        StopInGameLoop(); // Cancel any existing loop
        _roundCancellation = new CancellationTokenSource();

        try
        {
            // Loop until round ends or cancellation
            while (!_roundCancellation.Token.IsCancellationRequested)
            {
                // Random delay to simulate human thinking
                var delay = Random.Shared.Next(_settings.MinThinkTimeMs, _settings.MaxThinkTimeMs);
                await Task.Delay(delay, _roundCancellation.Token);

                var gameState = BuildGameState();
                var playerId = Guid.Parse(_applicationVM.Player.Id);
                var action = _brain.DecideAction(gameState, playerId);

                if (action != null && action is not AgentAction.WaitAction)
                {
                    await ExecuteAction(action);
                }

                // Exit if no actions remain (avoid unnecessary loops)
                if (NoActionsAvailable(gameState))
                {
                    _logger.LogInformation("[AutoPilot] No actions remaining, exiting loop early");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[AutoPilot] InGame loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoPilot] Error in InGame loop");
        }
    }

    private void StopInGameLoop()
    {
        _roundCancellation?.Cancel();
        _roundCancellation?.Dispose();
        _roundCancellation = null;
    }

    private async Task HandleStateChange(GameStates newState)
    {
        // Always stop InGame loop on state change
        StopInGameLoop();

        // Wait a bit before deciding on action in new state
        await Task.Delay(Random.Shared.Next(_settings.MinThinkTimeMs, _settings.MaxThinkTimeMs));

        try
        {
            var gameState = BuildGameState();
            var playerId = Guid.Parse(_applicationVM.Player.Id);
            var action = _brain.DecideAction(gameState, playerId);

            if (action != null && action is not AgentAction.WaitAction)
            {
                await ExecuteAction(action);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoPilot] Error handling state change to {State}", newState);
        }
    }

    private AgentGameState BuildGameState()
    {
        return new AgentGameState(
            CurrentGameState: _gameStateMachineVM.GameState,
            CardsInHand: _gameplayVM.CardsInHand
                .Select(c => new CardDTO { Rank = c.Rank, Suit = c.Suit, IsFaceDown = c.IsFaceDown })
                .ToList(),
            AvailablePlayHands: _gameplayVM.AvailablePlayHands,
            AvailableDiscards: _gameplayVM.AvailableDiscards,
            PowerCharges: _gameplayVM.PowerCharges,
            ActivePlayerPower: _gameplayVM.ActivePlayerPower,
            Score: _gameplayVM.Score,
            RunTimeInSeconds: _gameplayVM.RunTimeInSeconds,
            AvailableShopItems: _gameDataStore.ShopItems.Select(x => x.Root!).ToList(),
            AvailablePlayerPowers: _gameDataStore.PlayerPowers
                .Select(p => new PlayerPowerDTO
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    PointCost = p.PointCost
                })
                .ToList(),
            Wallet: _gameDataStore.Wallet
        );
    }

    private async Task ExecuteAction(AgentAction action)
    {
        var playerId = _applicationVM.Player.Id;
        var gameId = _gameDataStore.GameId;

        try
        {
            // Check SignalR connection
            if (!_signalR.IsConnected)
            {
                _logger.LogWarning("[AutoPilot] SignalR not connected, skipping action {ActionType}", action.GetType().Name);
                return;
            }

            // Validate state before executing action
            if (!ValidateAction(action))
            {
                _logger.LogWarning("[AutoPilot] Action {ActionType} not valid in current state", action.GetType().Name);
                return;
            }

            switch (action)
            {
                case AgentAction.PlayHandAction playHand:
                    _logger.LogInformation("[AutoPilot] Playing hand with {Count} cards", playHand.Cards.Count);
                    await _signalR.PlayHandAsync(playerId, playHand.Cards);
                    break;

                case AgentAction.DiscardAction discard:
                    _logger.LogInformation("[AutoPilot] Discarding {Count} cards", discard.Cards.Count);
                    await _signalR.DiscardAsync(playerId, discard.Cards);
                    break;

                case AgentAction.ActivatePowerAction:
                    _logger.LogInformation("[AutoPilot] Activating player power");
                    if (!string.IsNullOrEmpty(gameId))
                    {
                        await _signalR.ActivatePlayerPowerAsync(gameId, playerId);
                    }
                    break;

                case AgentAction.SelectPlayerPowerAction selectPower:
                    _logger.LogInformation("[AutoPilot] Selecting power {PowerId}", selectPower.PlayerPowerId);
                    if (!string.IsNullOrEmpty(gameId))
                    {
                        var result = await _playerPowerEndpoints.SelectPlayerPowerAsync(
                            gameId,
                            playerId,
                            selectPower.PlayerPowerId
                        );

                        if (result.IsSuccess)
                        {
                            _logger.LogInformation("[AutoPilot] Successfully selected power {PowerId}", selectPower.PlayerPowerId);
                        }
                        else
                        {
                            _logger.LogWarning("[AutoPilot] Failed to select power {PowerId}", selectPower.PlayerPowerId);
                        }
                    }
                    break;

                case AgentAction.PurchaseShopItemAction purchase:
                    _logger.LogInformation("[AutoPilot] Purchasing item {ItemId}", purchase.ItemId);
                    // TODO: Implement shop purchase once endpoint is identified
                    _logger.LogWarning("[AutoPilot] PurchaseShopItemAction not yet implemented");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoPilot] Failed to execute action {ActionType}", action.GetType().Name);
        }
    }

    private bool ValidateAction(AgentAction action)
    {
        var currentState = _gameStateMachineVM.GameState;

        return action switch
        {
            AgentAction.PlayHandAction or AgentAction.DiscardAction or AgentAction.ActivatePowerAction
                => currentState == GameStates.InGame,
            AgentAction.SelectPlayerPowerAction
                => currentState == GameStates.Freebie,
            AgentAction.PurchaseShopItemAction
                => currentState == GameStates.Shop,
            _ => true
        };
    }

    private bool NoActionsAvailable(AgentGameState state)
    {
        return state.AvailablePlayHands == 0 &&
               state.AvailableDiscards == 0 &&
               state.PowerCharges == 0;
    }

    public void Dispose()
    {
        StopInGameLoop();
        _signalR.HandleNotificationReceived -= OnNotificationReceived;
    }
}
