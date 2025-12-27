using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using ChefKnifeStudios.PokerAttack.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface ILobbyAutomationService
{
    Task StartAutomationAsync(CancellationToken cancellationToken = default);
}

public class LobbyAutomationService : ILobbyAutomationService
{
    private readonly ILobbyViewModel _lobbyViewModel;
    private readonly ISignalRNotificationService _signalR;
    private readonly IApplicationViewModel _applicationVM;
    private readonly AgentSettings _settings;
    private readonly ILogger<LobbyAutomationService> _logger;

    public LobbyAutomationService(
        ILobbyViewModel lobbyViewModel,
        ISignalRNotificationService signalR,
        IApplicationViewModel applicationVM,
        AgentSettings settings,
        ILogger<LobbyAutomationService> logger)
    {
        _lobbyViewModel = lobbyViewModel;
        _signalR = signalR;
        _applicationVM = applicationVM;
        _settings = settings;
        _logger = logger;
    }

    public async Task StartAutomationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[LobbyAutomation] Starting automation for player {PlayerId}", _applicationVM.Player.Id);

            // Wait for SignalR connection to be established (max 10 seconds)
            var waitStart = DateTime.UtcNow;
            while (!_signalR.IsConnected && (DateTime.UtcNow - waitStart).TotalSeconds < 10)
            {
                _logger.LogInformation("[LobbyAutomation] Waiting for SignalR connection...");
                await Task.Delay(500, cancellationToken);
            }

            if (!_signalR.IsConnected)
            {
                _logger.LogError("[LobbyAutomation] SignalR connection not established after 10 seconds, aborting automation");
                return;
            }

            _logger.LogInformation("[LobbyAutomation] SignalR connected, proceeding with lobby automation");

            // Load available lobbies
            await _lobbyViewModel.LoadLobbiesAsync(cancellationToken);

            // Find first available lobby with space
            var availableLobby = _lobbyViewModel.Lobbies
                .FirstOrDefault(l => l.Players.Count < _settings.MaxPlayersBeforeStart);

            if (availableLobby != null && _settings.AutoJoinLobby)
            {
                _logger.LogInformation("[LobbyAutomation] Joining lobby {LobbyId} with {PlayerCount} players",
                    availableLobby.Id, availableLobby.Players.Count);

                await _lobbyViewModel.JoinLobbyAsync(availableLobby.Id, _applicationVM.Player, cancellationToken);
                await _signalR.JoinLobbyGroupAsync(availableLobby.Id);

                // If we are the host and enough players have joined, start the game
                if (_settings.AutoStartGameAsHost &&
                    availableLobby.HostPlayer.Id == _applicationVM.Player.Id &&
                    availableLobby.Players.Count >= _settings.MaxPlayersBeforeStart)
                {
                    _logger.LogInformation("[LobbyAutomation] Starting game as host");

                    if (Enum.TryParse<GameModes>(_settings.DefaultGameMode, out var gameMode))
                    {
                        await _lobbyViewModel.StartGameAsync(availableLobby.Id, gameMode, cancellationToken);
                    }
                    else
                    {
                        _logger.LogWarning("[LobbyAutomation] Invalid game mode {GameMode}, defaulting to Quick",
                            _settings.DefaultGameMode);
                        await _lobbyViewModel.StartGameAsync(availableLobby.Id, GameModes.Quick, cancellationToken);
                    }
                }
            }
            else if (_settings.AutoCreateLobby)
            {
                _logger.LogInformation("[LobbyAutomation] No available lobbies, creating new lobby");
                await _lobbyViewModel.CreateLobbyAsync(_applicationVM.Player, cancellationToken);

                // After creating, find the lobby we just created and join its SignalR group
                await Task.Delay(1000, cancellationToken); // Give server time to create lobby
                await _lobbyViewModel.LoadLobbiesAsync(cancellationToken);

                var createdLobby = _lobbyViewModel.Lobbies
                    .FirstOrDefault(l => l.HostPlayer.Id == _applicationVM.Player.Id);

                if (createdLobby != null)
                {
                    _logger.LogInformation("[LobbyAutomation] Joining SignalR group for created lobby {LobbyId}", createdLobby.Id);
                    await _signalR.JoinLobbyGroupAsync(createdLobby.Id);

                    // Subscribe to lobby updates to detect when enough players join
                    _signalR.HandleNotificationReceived += async (notification) =>
                    {
                        if (notification.NotificationType == PokerAttackNotificationType.PlayerJoined)
                        {
                            _logger.LogInformation("[LobbyAutomation] Player joined, reloading lobby state");
                            await _lobbyViewModel.LoadLobbiesAsync(cancellationToken);

                            var lobby = _lobbyViewModel.Lobbies.FirstOrDefault(l => l.Id == createdLobby.Id);
                            if (lobby != null &&
                                _settings.AutoStartGameAsHost &&
                                lobby.Players.Count >= _settings.MaxPlayersBeforeStart)
                            {
                                _logger.LogInformation("[LobbyAutomation] Lobby has {Count} players, starting game", lobby.Players.Count);

                                if (Enum.TryParse<GameModes>(_settings.DefaultGameMode, out var gameMode))
                                {
                                    await _lobbyViewModel.StartGameAsync(lobby.Id, gameMode, cancellationToken);
                                }
                                else
                                {
                                    await _lobbyViewModel.StartGameAsync(lobby.Id, GameModes.Quick, cancellationToken);
                                }
                            }
                        }
                    };

                    _logger.LogInformation("[LobbyAutomation] Waiting for {Count} players before starting game", _settings.MaxPlayersBeforeStart);
                }
            }
            else
            {
                _logger.LogWarning("[LobbyAutomation] No available lobbies and AutoCreateLobby is disabled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LobbyAutomation] Error during lobby automation");
        }
    }
}
