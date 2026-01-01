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

    public event PokerAttackNotificationHandler? HandleNotificationReceived;

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

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(url)
                    .WithAutomaticReconnect()
                    .ConfigureLogging(logging => {
                        logging.SetMinimumLevel(LogLevel.Debug);
                    })
                    .Build();

                _logger.LogInformation("Connecting to SignalR hub: {host}", baseUri.Host);

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
}
