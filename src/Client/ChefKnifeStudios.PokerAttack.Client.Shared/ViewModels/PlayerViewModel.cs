using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IPlayerViewModel : IViewModel
{
    string GameId { get; }
    bool IsLoadingWallet { get; }
    int Wallet { get; }
    void Init(string gameId);
    Task UpdatePlayerNameAsync(PlayerDTO player, string newName, CancellationToken cancellationToken = default);
}

public partial class PlayerViewModel : BaseViewModel, IPlayerViewModel
{
    readonly IApplicationViewModel _applicationViewModel;
    readonly IGameplayEndpointsService _gameplayEndpointsService;
    readonly ILobbyEndpointsService _lobbyEndpointsService;
    readonly ISignalRNotificationService _signalRNotificationService;

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
        ISignalRNotificationService signalRNotificationService)
    {
        _applicationViewModel = applicationViewModel;
        _gameplayEndpointsService = gameplayEndpointsService;
        _lobbyEndpointsService = lobbyEndpointsService;
        _signalRNotificationService = signalRNotificationService;

        _signalRNotificationService.HandleNotificationReceived += HandleSignalRNotificationReceived;
    }

    public void Dispose()
    {
        _signalRNotificationService.HandleNotificationReceived -= HandleSignalRNotificationReceived;
    }

    public async Task UpdatePlayerNameAsync(PlayerDTO player, string newName, CancellationToken cancellationToken = default)
    {
        player.Name = newName;
        _ = await _lobbyEndpointsService.UpdatePlayerAsync(player, cancellationToken);
    }

    public async Task LoadWalletAsync(CancellationToken cancellationToken = default)
    {
        if (GameId is null) throw new ApplicationException("ScoreboardViewModel must Init before loading rounds.");
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
