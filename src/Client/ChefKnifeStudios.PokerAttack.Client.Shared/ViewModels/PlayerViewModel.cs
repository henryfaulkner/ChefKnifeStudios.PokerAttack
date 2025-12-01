using Blazored.LocalStorage;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using NameGenerator.Generators;
using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IPlayerViewModel : IViewModel
{
    string GameId { get; }
    bool IsLoadingWallet { get; }
    int Wallet { get; }
    void Init(string gameId);
    Task UpdatePlayerNameAsync(PlayerDTO player, string newName, CancellationToken cancellationToken = default);
    Task<string> RandomizePlayerNameAsync(PlayerDTO player, CancellationToken cancellationToken = default);
}

public partial class PlayerViewModel : BaseViewModel, IPlayerViewModel
{
    readonly IApplicationViewModel _applicationViewModel;
    readonly IGameplayEndpointsService _gameplayEndpointsService;
    readonly ILobbyEndpointsService _lobbyEndpointsService;
    readonly ISignalRNotificationService _signalRNotificationService;
    readonly ILocalStorageService _localStorageService;

    [ObservableProperty]
    string? _gameId;

    [ObservableProperty]
    bool _isLoadingWallet = false;

    [ObservableProperty]
    int _wallet = 0;

    public void Init(string gameId)
    {
        GameId = gameId;
    }

    public PlayerViewModel(
        IApplicationViewModel applicationViewModel,
        IGameplayEndpointsService gameplayEndpointsService,
        ILobbyEndpointsService lobbyEndpointsService,
        ISignalRNotificationService signalRNotificationService,
        ILocalStorageService localStorageService)
    {
        _applicationViewModel = applicationViewModel;
        _gameplayEndpointsService = gameplayEndpointsService;
        _lobbyEndpointsService = lobbyEndpointsService;
        _signalRNotificationService = signalRNotificationService;
        _localStorageService = localStorageService;

        _signalRNotificationService.HandleNotificationReceived += HandleSignalRNotificationReceived;
    }

    public void Dispose()
    {
        _signalRNotificationService.HandleNotificationReceived -= HandleSignalRNotificationReceived;
    }

    public async Task UpdatePlayerNameAsync(PlayerDTO player, string newName, CancellationToken cancellationToken = default)
    {
        player.Name = newName;
        await _localStorageService.SetItemAsync(LocalStorageConstants.PlayerNameKey, newName, cancellationToken);
        _ = await _lobbyEndpointsService.UpdatePlayerAsync(player, cancellationToken);
    }

    public async Task<string> RandomizePlayerNameAsync(PlayerDTO player, CancellationToken cancellationToken = default)
    {
        var generator = new GamerTagGenerator();
        string result = generator.Generate();

        player.Name = result;
        await _localStorageService.SetItemAsync(LocalStorageConstants.PlayerNameKey, result, cancellationToken);
        _ = await _lobbyEndpointsService.UpdatePlayerAsync(player, cancellationToken);
        return result;
    }

    public async Task LoadWalletAsync(CancellationToken cancellationToken = default)
    {
        if (GameId is null) throw new ApplicationException("PlayerViewModel must Init before loading wallets.");
        IsLoadingWallet = true;
        Wallet = (await _gameplayEndpointsService.GetPlayerWalletAsync(
            GameId,
            _applicationViewModel.Player.Id,
            cancellationToken
        )).Value ?? 0;
        IsLoadingWallet = false;
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
        }
    }
}
