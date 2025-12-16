using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR.EventArgs;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface IGameDataService : IDisposable
{
    Task LoadWalletAsync(CancellationToken cancellationToken = default);
    Task LoadShopItemsAsync(CancellationToken cancellationToken = default);
    Task LoadPlayerPowersAsync(CancellationToken cancellationToken = default);
    Task LoadScoreboardAsync(CancellationToken cancellationToken = default);
    Task PurchaseShopItemAsync(ShopItem item, CancellationToken cancellationToken = default);
    Task SelectPlayerPowerAsync(PlayerPowerListItem playerPower, CancellationToken cancellationToken = default);
}

public class GameDataService : IGameDataService
{
    readonly ILogger<GameDataService> _logger;
    readonly IApplicationViewModel _applicationViewModel;
    readonly IGameplayEndpointsService _gameplayEndpointsService;
    readonly IShopEndpointsService _shopEndpointsService;
    readonly IPlayerPowerEndpointsService _playerPowerEndpointsService;
    readonly IToastService _toastService;
    readonly IEventNotificationService _eventNotificationService;
    readonly ISignalRNotificationService _signalRNotificationService;
    readonly IGameDataStore _gameDataStore;

    public GameDataService(
        ILogger<GameDataService> logger,
        IApplicationViewModel applicationViewModel,
        IGameplayEndpointsService gameplayEndpointsService,
        IShopEndpointsService shopEndpointsService,
        IPlayerPowerEndpointsService playerPowerEndpointsService,
        IToastService toastService,
        IEventNotificationService eventNotificationService,
        ISignalRNotificationService signalRNotificationService,
        IGameDataStore gameDataStore)
    {
        _logger = logger;
        _applicationViewModel = applicationViewModel;
        _gameplayEndpointsService = gameplayEndpointsService;
        _shopEndpointsService = shopEndpointsService;
        _playerPowerEndpointsService = playerPowerEndpointsService;
        _toastService = toastService;
        _eventNotificationService = eventNotificationService;
        _signalRNotificationService = signalRNotificationService;
        _gameDataStore = gameDataStore;

        _signalRNotificationService.HandleNotificationReceived += HandleSignalRNotificationReceived;
    }

    public void Dispose()
    {
        _signalRNotificationService.HandleNotificationReceived -= HandleSignalRNotificationReceived;
    }

    public async Task LoadWalletAsync(CancellationToken cancellationToken = default)
    {
        if (_gameDataStore.GameId is null) throw new ApplicationException("GameDataStore.GameId must be set before loading wallets.");
        _gameDataStore.IsLoadingWallet = true;
        _gameDataStore.Wallet = (await _gameplayEndpointsService.GetPlayerWalletAsync(
            _gameDataStore.GameId,
            _applicationViewModel.Player.Id,
            cancellationToken
        )).Value ?? 0;
        _gameDataStore.IsLoadingWallet = false;
    }

    public async Task LoadShopItemsAsync(CancellationToken cancellationToken = default)
    {
        if (_gameDataStore.GameId is null) throw new ApplicationException("GameDataStore.GameId must be set before loading shop items.");
        _gameDataStore.IsLoadingShop = true;
        _gameDataStore.ShopItems = (await _shopEndpointsService.GetShopItemsAsync(
            _gameDataStore.GameId,
            cancellationToken
        )).Value?
            .Select(x => new ShopItem(x))
            .ToObservableCollection() ?? [];
        _gameDataStore.IsLoadingShop = false;
    }

    public async Task LoadPlayerPowersAsync(CancellationToken cancellationToken = default)
    {
        _gameDataStore.IsLoadingPlayerPowers = true;
        var playerPowers = (await _playerPowerEndpointsService.GetPlayerPowersAsync()).Value;
        _gameDataStore.PlayerPowers = playerPowers?.Select(x => new PlayerPowerListItem(x)).ToObservableCollection() ?? [];
        _gameDataStore.IsLoadingPlayerPowers = false;
    }

    public async Task LoadScoreboardAsync(CancellationToken cancellationToken = default)
    {
        if (_gameDataStore.GameId is null) throw new ApplicationException("GameDataStore.GameId must be set before loading rounds.");
        _gameDataStore.IsLoadingScoreboard = true;
        var round = (await _gameplayEndpointsService.GetLatestRoundAsync(_gameDataStore.GameId, cancellationToken)).Value;
        _gameDataStore.ScoreboardItems = round?.Scores.Select(x => new ScoreboardListItem(x)).ToObservableCollection() ?? [];
        _gameDataStore.IsLoadingScoreboard = false;
    }

    public async Task PurchaseShopItemAsync(ShopItem item, CancellationToken cancellationToken = default)
    {
        if (_gameDataStore.GameId is null) throw new ApplicationException("GameDataStore.GameId must be set before purchasing items.");
        if (_gameDataStore.Wallet < item.Price)
        {
            _toastService.ShowWarning("Not enough funds to purchase item");
            return;
        }

        var result = await _shopEndpointsService.PurchaseShopItemAsync(
            _gameDataStore.GameId,
            _applicationViewModel.Player.Id,
            item.ItemId,
            cancellationToken
        );

        if (result.IsSuccess && result.Value is not null)
        {
            _gameDataStore.Wallet -= item.Price;
            item.WasPurchased = true;
            _gameDataStore.PlayerItems.Add(item);
        }
        else
        {
            _toastService.ShowError("Failed to purchase item");
        }
    }

    public async Task SelectPlayerPowerAsync(PlayerPowerListItem playerPower, CancellationToken cancellationToken = default)
    {
        if (_gameDataStore.GameId is null) throw new ApplicationException("GameDataStore.GameId must be set before submitting player power.");
        try
        {
            foreach (var pp in _gameDataStore.PlayerPowers) pp.IsSelected = false;
            playerPower.IsSelected = true;
            if (playerPower is null)
            {
                _toastService.ShowWarning("Select a power");
                return;
            }

            var res = await _playerPowerEndpointsService.SelectPlayerPowerAsync(
                _gameDataStore.GameId,
                _applicationViewModel.Player.Id,
                playerPower.Id,
                cancellationToken
            );

            if (!res.IsSuccess && res.Value is not PlayerPowerDTO)
            {
                _toastService.ShowError("Player power was not selected");
                playerPower.IsSelected = false;
                return;
            }

            _eventNotificationService.PostEvent(
                this,
                new PlayerPowerEventArgs
                {
                    Type = PlayerPowerEventArgs.EventTypes.Selection,
                    Data = new PlayerPowerEventArgs.EventData { PlayerPower = res.Value },
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred");
        }
    }

    async Task HandleSignalRNotificationReceived(PokerAttackNotification notification)
    {
        switch (notification.NotificationType)
        {
            case PokerAttackNotificationType.GameStateChanged:
                var gameState = JsonSerializer.Deserialize<GameStates>(notification.Payload!, JsonOptions.Get());
                if (gameState == GameStates.Shop)
                {
                    await LoadWalletAsync();
                }
                break;

            case PokerAttackNotificationType.EliminationStarted:
                {
                    if (string.IsNullOrWhiteSpace(notification.Payload))
                        break;

                    var args = JsonSerializer.Deserialize<EliminationStartedArgs>(notification.Payload!);
                    if (args?.Players == null)
                        break;

                    var ids = args.Players.Select(p => p.PlayerId).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // Mark items that are starting elimination
                    foreach (var item in _gameDataStore.ScoreboardItems)
                    {
                        item.IsEliminating = ids.Contains(item.ClientUserId);
                    }
                    break;
                }

            case PokerAttackNotificationType.EliminationFinished:
                {
                    if (string.IsNullOrWhiteSpace(notification.Payload))
                        break;

                    var args = JsonSerializer.Deserialize<EliminationFinishedArgs>(notification.Payload!);
                    if (args?.Losers == null)
                        break;
                    break;
                }
        }
    }
}
