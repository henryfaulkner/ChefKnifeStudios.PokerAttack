using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services;

public delegate Task PokerAttackNotificationHandler(PokerAttackNotification notification);

public interface ISignalRNotificationService
{
    event PokerAttackNotificationHandler? HandleNotificationReceived;
    event Func<Task>? OnReconnectionSuccess;

    Task InitAsync(string playerId);
    Task JoinLobbyGroupAsync(string lobbyId);
    Task LeaveLobbyGroupAsync(string lobbyId);
    Task JoinGameGroupAsync(string gameId);
    Task LeaveGameGroupAsync(string gameId);
    Task StartGameAsync(string gameId, string playerId);
    Task StartRoundAsync(string gameId, string playerId);
    Task PlayHandAsync(string playerId, List<CardDTO> hand);
    Task DiscardAsync(string playerId, List<CardDTO> discardCards);
    Task ActivatePlayerPowerAsync(string gameId, string playerId);
    Task TransitionGameStateAsync(string gameId, GameEvents gameEvent);

    // TODO: remove for server-run implimentation
    Task EndGameAsync(string gameId);
    Task LeaveGameAsync(string gameId, string playerId);
}

public class SignalRNotificationService : ISignalRNotificationService, IDisposable
{
    private HubConnection? _hubConnection;

    private readonly IConfiguration _configuration;
    private readonly IWebAssemblyHostEnvironment _hostEnvironment;
    private readonly ILogger<SignalRNotificationService> _logger;

    // Connection state tracking
    private string? _playerId;
    private string? _currentLobbyId;
    private string? _currentGameId;
    private bool _isReconnecting = false;

    public event PokerAttackNotificationHandler? HandleNotificationReceived;
    public event Func<Task>? OnReconnectionSuccess;

    public SignalRNotificationService(
        IConfiguration configuration,
        IWebAssemblyHostEnvironment hostEnvironment,
        ILogger<SignalRNotificationService> logger)
    {
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public async Task InitAsync(string playerId)
    {
        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
            return;

        try
        {
            CloseConnection();

            _playerId = playerId;

            var apis = _configuration.GetSection("AppSettings:ExternalApis");
            var itemArray = apis.GetChildren();

            var setting = itemArray.FirstOrDefault(a =>
                a.GetValue<string>("Name") == nameof(APIs.PokerAttackSignalR));

            if (setting != null)
            {
                var baseUrl = setting.GetValue("BaseUri", string.Empty)?.TrimEnd('/');
                if (baseUrl is null)
                {
                    string errMsg = "BaseUrl for PokerAttackSignalR API config is null.";
                    _logger.LogCritical(errMsg);
                    throw new ApplicationException(errMsg);
                }

                Uri baseUri;
                if (Uri.IsWellFormedUriString(baseUrl, UriKind.Absolute))
                {
                    baseUri = new Uri(baseUrl);
                }
                else
                {
                    var hostUri = new Uri(_hostEnvironment.BaseAddress, UriKind.Absolute);
                    var relativeUri = new Uri(baseUrl, UriKind.Relative);
                    baseUri = new Uri(hostUri, relativeUri);
                }

                var url = $"{baseUri.ToString().TrimEnd('/')}/cks-notification?playerId={playerId}";

                // Custom reconnection policy with extended retry intervals for mobile backgrounding
                var reconnectPolicy = new ExtendedRetryPolicy();

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(url)
                    .WithAutomaticReconnect(reconnectPolicy)
                    .ConfigureLogging(logging => {
                        logging.SetMinimumLevel(LogLevel.Debug);
                    })
                    .Build();

                _logger.LogInformation("Connecting to SignalR hub: {host}", baseUri.Host);

                // Set up connection lifecycle event handlers
                _hubConnection.Reconnecting += OnReconnecting;
                _hubConnection.Reconnected += OnReconnected;
                _hubConnection.Closed += OnClosed;

                _hubConnection.On<PokerAttackNotification>("ReceivePokerAttackNotification", notification =>
                {
                    HandleNotificationReceived?.Invoke(notification);
                });

                await _hubConnection.StartAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing SignalR Notification Hub");
            _hubConnection = null;
        }
    }

    public void Dispose()
    {
        try
        {
            CloseConnection();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing SignalR connection");
            throw;
        }
    }

    private void CloseConnection()
    {
        try
        {
            if (_hubConnection == null) return;
            _ = _hubConnection.StopAsync();
            _hubConnection = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing SignalR connection");
            throw;
        }
    }

    #region Lobby Notifications
    public async Task JoinLobbyGroupAsync(string lobbyId)
    {
        try
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("SignalR connection is not established.");

            await _hubConnection.InvokeAsync("JoinLobbyGroupAsync", lobbyId);
            _currentLobbyId = lobbyId;
            _logger.LogInformation("Joined lobby group: {lobbyId}", lobbyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining lobby group {lobbyId}", lobbyId);
            throw;
        }
    }

    public async Task LeaveLobbyGroupAsync(string lobbyId)
    {
        try
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("SignalR connection is not established.");

            await _hubConnection.InvokeAsync("LeaveLobbyGroupAsync", lobbyId);
            _currentLobbyId = null;
            _logger.LogInformation("Left lobby group: {lobbyId}", lobbyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving lobby group {lobbyId}", lobbyId);
            throw;
        }
    }
    #endregion

    #region Gameplay Notifications
    public async Task JoinGameGroupAsync(string gameId)
    {
        try
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("SignalR connection is not established.");

            await _hubConnection.InvokeAsync("JoinGameGroupAsync", gameId);
            _currentGameId = gameId;
            _currentLobbyId = null; // Clear lobby when joining game
            _logger.LogInformation("Joined game group: {gameId}", gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining game group {gameId}", gameId);
            throw;
        }
    }

    public async Task LeaveGameGroupAsync(string gameId)
    {
        try
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("SignalR connection is not established.");

            await _hubConnection.InvokeAsync("LeaveGameGroupAsync", gameId);
            _currentGameId = null;
            _logger.LogInformation("Left game group: {gameId}", gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving game group {gameId}", gameId);
            throw;
        }
    }

    public async Task StartGameAsync(string gameId, string playerId)
    {
        try
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("SignalR connection is not established.");

            await _hubConnection.InvokeAsync("StartGame", gameId, playerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting game {gameId}", gameId);
            throw;
        }
    }

    public async Task StartRoundAsync(string gameId, string playerId)
    {
        try
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("SignalR connection is not established.");

            await _hubConnection.InvokeAsync("StartRound", gameId, playerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting round for game {gameId}", gameId);
            throw;
        }
    }

    public async Task PlayHandAsync(string playerId, List<CardDTO> hand)
    {
        try
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("SignalR connection is not established.");

            await _hubConnection.InvokeAsync("PlayHand", playerId, hand);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing hand for player {playerId}", playerId);
            throw;
        }
    }

    public async Task DiscardAsync(string playerId, List<CardDTO> discardCards)
    {
        try
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("SignalR connection is not established.");

            await _hubConnection.InvokeAsync("Discard", playerId, discardCards);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discarding cards for player {playerId}", playerId);
            throw;
        }
    }

    public async Task ActivatePlayerPowerAsync(string gameId, string playerId)
    {
        try
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("SignalR connection is not established.");

            await _hubConnection.InvokeAsync("ActivatePlayerPower", gameId, playerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating player power for player {playerId} in game {gameId}", playerId, gameId);
            throw;
        }
    }
    #endregion

    public async Task TransitionGameStateAsync(string gameId, GameEvents gameEvent)
    {
        try
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("SignalR connection is not established.");

            await _hubConnection.InvokeAsync("TransitionGameState", gameId, gameEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transitioning game state for game {gameId} with event {gameEvent}", gameId, gameEvent);
            throw;
        }
    }

    public async Task EndGameAsync(string gameId)
    {
        try
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("SignalR connection is not established.");

            await _hubConnection.InvokeAsync("EndGame", gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending game {gameId}", gameId);
            throw;
        }
    }

    public async Task LeaveGameAsync(string gameId, string playerId)
    {
        try
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("SignalR connection is not established.");

            await _hubConnection.InvokeAsync("LeaveGame", gameId, playerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving game {gameId} for player {playerId}", gameId, playerId);
            throw;
        }
    }

    #region Reconnection Event Handlers

    private Task OnReconnecting(Exception? exception)
    {
        _isReconnecting = true;
        _logger.LogWarning(exception, "SignalR connection lost. Attempting to reconnect...");
        return Task.CompletedTask;
    }

    private async Task OnReconnected(string? connectionId)
    {
        _isReconnecting = false;
        _logger.LogInformation("SignalR reconnected successfully. ConnectionId: {connectionId}", connectionId);

        // Re-join groups after reconnection
        await RejoinGroupsAfterReconnection();

        // Notify listeners that reconnection succeeded
        if (OnReconnectionSuccess != null)
        {
            await OnReconnectionSuccess.Invoke();
        }
    }

    private Task OnClosed(Exception? exception)
    {
        _isReconnecting = false;
        if (exception != null)
        {
            _logger.LogError(exception, "SignalR connection closed with error");
        }
        else
        {
            _logger.LogInformation("SignalR connection closed");
        }
        return Task.CompletedTask;
    }

    private async Task RejoinGroupsAfterReconnection()
    {
        try
        {
            // Re-join game group if we were in one
            if (!string.IsNullOrEmpty(_currentGameId))
            {
                _logger.LogInformation("Re-joining game group after reconnection: {gameId}", _currentGameId);
                await _hubConnection!.InvokeAsync("JoinGameGroupAsync", _currentGameId);
            }
            // Otherwise, re-join lobby group if we were in one
            else if (!string.IsNullOrEmpty(_currentLobbyId))
            {
                _logger.LogInformation("Re-joining lobby group after reconnection: {lobbyId}", _currentLobbyId);
                await _hubConnection!.InvokeAsync("JoinLobbyGroupAsync", _currentLobbyId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error re-joining groups after reconnection");
        }
    }

    #endregion
}

/// <summary>
/// Custom reconnection policy with extended retry intervals suitable for mobile backgrounding.
/// Retries: 0s, 2s, 10s, 30s, 60s, then every 2 minutes up to 30 minutes total.
/// </summary>
internal class ExtendedRetryPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        // Retry attempts: 0s, 2s, 10s, 30s, 60s, then every 2 minutes
        return retryContext.PreviousRetryCount switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromSeconds(2),
            2 => TimeSpan.FromSeconds(10),
            3 => TimeSpan.FromSeconds(30),
            4 => TimeSpan.FromSeconds(60),
            _ when retryContext.ElapsedTime < TimeSpan.FromMinutes(30) => TimeSpan.FromMinutes(2),
            _ => null // Stop retrying after 30 minutes
        };
    }
}
