using AutoFixture;
using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR.EventArgs;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public partial class LobbyListItem : ObservableObject
{
    [SetsRequiredMembers]
    public LobbyListItem(LobbyDTO lobby)
    {
        _id = lobby.Id;
        _hostPlayer = lobby.HostPlayer;
        _players = lobby.Players;
        _gameId = lobby.GameId;
    }

    [ObservableProperty]
    public required string _id;
    [ObservableProperty]
    public required PlayerDTO _hostPlayer;
    [ObservableProperty]
    public HashSet<PlayerDTO> _players = new();
    [ObservableProperty]
    public string? _gameId;
}

public interface ILobbyViewModel : IViewModel
{
    ObservableCollection<LobbyListItem> Lobbies { get; }
    bool IsLoadingLobbies { get; }
    Task LoadLobbiesAsync(CancellationToken cancellationToken = default);
    Task CreateLobbyAsync(PlayerDTO player, CancellationToken cancellationToken = default);
    Task JoinLobbyAsync(string lobbyId, PlayerDTO player, CancellationToken cancellationToken = default);
    Task LeaveLobbyAsync(string lobbyId, PlayerDTO player, CancellationToken cancellationToken = default);
    Task ShutdownLobbyAsync(string lobbyId, CancellationToken cancellationToken = default);
    Task StartGameAsync(string lobbyId, GameModes gameMode, CancellationToken cancellationToken = default);
    Task StartLobbyPollingAsync(CancellationToken cancellationToken = default);
    void StopLobbyPolling();
}

public partial class LobbyViewModel : BaseViewModel, ILobbyViewModel, IDisposable
{
    readonly ILobbyEndpointsService _lobbyEndpointsService;
    readonly ISignalRNotificationService _signalRNotificationService;
    readonly NavigationManager _navigationManager;
    readonly IToastService _toastService;
    readonly ILogger<LobbyViewModel> _logger;

    private Timer? _lobbyPollTimer;
    private const int POLL_INTERVAL_MS = 2000; // Poll every 2 seconds

    [ObservableProperty]
    ObservableCollection<LobbyListItem> _lobbies = [];

    [ObservableProperty]
    bool _isLoadingLobbies = false;

    public LobbyViewModel(
        ILobbyEndpointsService lobbyEndpointsService,
        ISignalRNotificationService signalRNotificationService,
        NavigationManager navigationManager,
        IToastService toastService,
        ILogger<LobbyViewModel> logger)
    {
        _lobbyEndpointsService = lobbyEndpointsService;
        _signalRNotificationService = signalRNotificationService;
        _navigationManager = navigationManager;
        _toastService = toastService;
        _logger = logger;

        _signalRNotificationService.HandleNotificationReceived += HandleSignalRNotificationReceived;
    }

    public void Dispose()
    {
        StopLobbyPolling();
        _signalRNotificationService.HandleNotificationReceived -= HandleSignalRNotificationReceived;
    }

    public async Task LoadLobbiesAsync(CancellationToken cancellationToken = default)
    {
        IsLoadingLobbies = true;
        await RefreshLobbiesFromServerAsync(cancellationToken);
        IsLoadingLobbies = false;
    }

    public async Task CreateLobbyAsync(PlayerDTO player, CancellationToken cancellationToken = default)
    {
        var res = await _lobbyEndpointsService.CreateLobbyAsync(new(player), cancellationToken);
        if (!res.IsSuccess) _toastService.ShowError("Lobby could not be created");
    }

    public async Task JoinLobbyAsync(string lobbyId, PlayerDTO player, CancellationToken cancellationToken = default)
    {
        var res = await _lobbyEndpointsService.AddPlayerAsync(new(lobbyId, player), cancellationToken);
        if (!res.IsSuccess) _toastService.ShowError("Lobby could not be joined");
    }

    public async Task LeaveLobbyAsync(string lobbyId, PlayerDTO player, CancellationToken cancellationToken = default)
    {
        var res = await _lobbyEndpointsService.RemovePlayerAsync(new(lobbyId, player), cancellationToken);
        if (!res.IsSuccess) _toastService.ShowError("Lobby could not be left");
    }

    public async Task ShutdownLobbyAsync(string lobbyId, CancellationToken cancellationToken = default)
    {
        var res = await _lobbyEndpointsService.ShutdownLobbyAsync(lobbyId, cancellationToken);
        if (!res.IsSuccess) _toastService.ShowError("Lobby could not be shutdown");
    }

    public async Task StartGameAsync(string lobbyId, GameModes gameMode, CancellationToken cancellationToken = default)
    {
        var result = await _lobbyEndpointsService.StartGameAsync(lobbyId, gameMode, cancellationToken);

        // Navigate immediately on success (fixes race condition where host misses SignalR notification)
        if (result.IsSuccess && !string.IsNullOrEmpty(result.Value))
        {
            _navigationManager.NavigateToGameplay(result.Value);
        }
        else if (!result.IsSuccess)
        {
            _toastService.ShowError("Failed to start game");
        }
    }

    public Task StartLobbyPollingAsync(CancellationToken cancellationToken = default)
    {
        _lobbyPollTimer?.Dispose();
        _lobbyPollTimer = new Timer(async _ =>
        {
            try
            {
                await RefreshLobbiesFromServerAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling lobby data");
            }
        }, null, 0, POLL_INTERVAL_MS);

        return Task.CompletedTask;
    }

    public void StopLobbyPolling()
    {
        _lobbyPollTimer?.Dispose();
        _lobbyPollTimer = null;
    }

    async Task RefreshLobbiesFromServerAsync(CancellationToken cancellationToken = default)
    {
        var res = await _lobbyEndpointsService.GetLobbiesAsync(cancellationToken);
        if (res.IsSuccess && res.Value is IEnumerable<LobbyDTO>)
        {
            var newLobbies = res.Value.Select(x => new LobbyListItem(x)).ToList();

            Lobbies.Clear();
            foreach (var lobby in newLobbies)
            {
                Lobbies.Add(lobby);
            }
        }
    }

    Task HandleSignalRNotificationReceived(PokerAttackNotification notification)
    {
        switch (notification.NotificationType)
        {
            case PokerAttackNotificationType.LobbyCreated:
                {
                    var args = JsonSerializer.Deserialize<LobbyEventArgs>(notification.Payload!, JsonOptions.Get());
                    Lobbies.Add(new LobbyListItem(args!.Lobby));
                    break;
                }
            case PokerAttackNotificationType.PlayerJoined:
            case PokerAttackNotificationType.PlayerLeft:
            case PokerAttackNotificationType.PlayerUpdated:
            case PokerAttackNotificationType.LobbiesChanged:
                {
                    var args = JsonSerializer.Deserialize<LobbyEventArgs>(notification.Payload!, JsonOptions.Get());
                    if (args?.Lobby is not null)
                    {
                        var index = Lobbies
                            .Select((lobby, i) => new { lobby, i })
                            .FirstOrDefault(x => x.lobby.Id == args.Lobby.Id)?.i ?? -1;

                        if (index >= 0)
                        {
                            Lobbies[index] = new LobbyListItem(args.Lobby);
                        }
                    }
                    break;
                }
            case PokerAttackNotificationType.LobbyShutdown:
                {
                    var args = JsonSerializer.Deserialize<LobbyEventArgs>(notification.Payload!, JsonOptions.Get());
                    var lobbyToRemove = Lobbies.FirstOrDefault(x => x.Id == args!.Lobby.Id);
                    if (lobbyToRemove != null)
                    {
                        Lobbies.Remove(lobbyToRemove);
                    }
                    break;
                }
            case PokerAttackNotificationType.GameStarted:
                {
                    var args = JsonSerializer.Deserialize<GameStartedEventArgs>(notification.Payload!, JsonOptions.Get());
                    if (args is { GameId: string gameId })
                    {
                        _navigationManager.NavigateToGameplay(gameId);
                    }
                    break;
                }
        }
        return Task.CompletedTask;
    }
}
