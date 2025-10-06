using AutoFixture;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using System.Text.Json;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR.EventArgs;
using ChefKnifeStudios.PokerAttack.Shared;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface ILobbyViewModel : IViewModel
{
    ObservableCollection<LobbyDTO> Lobbies { get; }
    bool IsLoadingLobbies { get; }
    Task LoadLobbiesAsync(CancellationToken cancellationToken = default);
    Task CreateLobbyAsync(PlayerDTO player, CancellationToken cancellationToken = default);
    Task JoinLobbyAsync(string gameId, PlayerDTO player, CancellationToken cancellationToken = default);
    Task LeaveLobbyAsync(string gameId, PlayerDTO player, CancellationToken cancellationToken = default);
    Task ShutdownLobbyAsync(string gameId, CancellationToken cancellationToken = default);
    Task StartGameAsync(string gameId, CancellationToken cancellationToken = default);
}

public partial class LobbyViewModel : BaseViewModel, ILobbyViewModel, IDisposable
{
    readonly ILobbyEndpointsService _lobbyEndpointsService;
    readonly ISignalRNotificationService _signalRNotificationService;
    readonly NavigationManager _navigationManager;

    [ObservableProperty]
    ObservableCollection<LobbyDTO> _lobbies = [];

    [ObservableProperty]
    bool _isLoadingLobbies = false;

    public LobbyViewModel(
        ILobbyEndpointsService lobbyEndpointsService,
        ISignalRNotificationService signalRNotificationService,
        NavigationManager navigationManager)
    {
        _lobbyEndpointsService = lobbyEndpointsService;
        _signalRNotificationService = signalRNotificationService;
        _navigationManager = navigationManager;

        _signalRNotificationService.HandleNotificationReceived += HandleSignalRNotificationReceived;
    }

    public void Dispose()
    {
        _signalRNotificationService.HandleNotificationReceived -= HandleSignalRNotificationReceived;
    }

    public async Task LoadLobbiesAsync(CancellationToken cancellationToken = default)
    {
        IsLoadingLobbies = true;

        var res = await _lobbyEndpointsService.GetLobbiesAsync(cancellationToken);
        if (res.IsSuccess && res.Value is IEnumerable<LobbyDTO>) 
            Lobbies = res.Value.ToObservableCollection();

        IsLoadingLobbies = false;
    }

    public async Task CreateLobbyAsync(PlayerDTO player, CancellationToken cancellationToken = default)
    {
        var res = await _lobbyEndpointsService.CreateLobbyAsync(new(player), cancellationToken);
        if (res.IsSuccess && res.Value is LobbyDTO lobby) await _signalRNotificationService.JoinGameGroupAsync(lobby.GameId);
    }

    public async Task JoinLobbyAsync(string gameId, PlayerDTO player, CancellationToken cancellationToken = default)
    {
        var res = await _lobbyEndpointsService.AddPlayerAsync(new(gameId, player), cancellationToken);
        if (res.IsSuccess) await _signalRNotificationService.JoinGameGroupAsync(gameId);
    }

    public async Task LeaveLobbyAsync(string gameId, PlayerDTO player, CancellationToken cancellationToken = default)
    {
        var res = await _lobbyEndpointsService.RemovePlayerAsync(new(gameId, player), cancellationToken);
        if (res.IsSuccess) await _signalRNotificationService.LeaveGameGroupAsync(gameId);
    }

    public async Task ShutdownLobbyAsync(string gameId, CancellationToken cancellationToken = default)
    {
        var res = await _lobbyEndpointsService.ShutdownLobbyAsync(gameId, cancellationToken);
        if (res.IsSuccess) await _signalRNotificationService.LeaveGameGroupAsync(gameId);
    }

    public async Task StartGameAsync(string gameId, CancellationToken cancellationToken = default) =>
        await _lobbyEndpointsService.StartGameAsync(gameId, cancellationToken);

    Task HandleSignalRNotificationReceived(PokerAttackNotification notification)
    {
        switch (notification.NotificationType)
        {
            case PokerAttackNotificationType.LobbyCreated:
                {
                    var args = JsonSerializer.Deserialize<LobbyEventArgs>(notification.Payload!, JsonOptions.Get());
                    Lobbies.Add(args!.Lobby);
                    break;
                }
            case PokerAttackNotificationType.PlayerJoined:
            case PokerAttackNotificationType.PlayerLeft:
            case PokerAttackNotificationType.PlayerUpdated:
                {
                    var args = JsonSerializer.Deserialize<LobbyEventArgs>(notification.Payload!, JsonOptions.Get());
                    if (args?.Lobby is not null)
                    {
                        var index = Lobbies
                            .Select((lobby, i) => new { lobby, i })
                            .FirstOrDefault(x => x.lobby.GameId == args.Lobby.GameId)?.i ?? -1;

                        if (index >= 0)
                        {
                            Lobbies[index] = args.Lobby;
                        }
                    }
                    break;
                }
            case PokerAttackNotificationType.LobbyShutdown:
                {
                    var args = JsonSerializer.Deserialize<LobbyEventArgs>(notification.Payload!, JsonOptions.Get());
                    var lobbyToRemove = Lobbies.FirstOrDefault(x => x.GameId == args!.Lobby.GameId);
                    if (lobbyToRemove != null)
                    {
                        Lobbies.Remove(lobbyToRemove);
                    }
                    break;
                }
            case PokerAttackNotificationType.GameStarted:
                {
                    var args = JsonSerializer.Deserialize<LobbyEventArgs>(notification.Payload!, JsonOptions.Get());
                    if (args is { Lobby: LobbyDTO lobby })
                    {
                        _navigationManager
                            .NavigateTo($"/gameplay?gameid={lobby.GameId}", replace: true);
                    }
                    break;
                }
        }
        return Task.CompletedTask;
    }
}
