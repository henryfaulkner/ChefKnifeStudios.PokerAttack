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

public delegate Task ConnectionStateChangedHandler(bool isReconnecting, bool retriesExhausted);

public interface ISignalRNotificationService
{
    event PokerAttackNotificationHandler? HandleNotificationReceived;

    /// <summary>
    /// Raised when the connection starts reconnecting, when it recovers, and when the retry
    /// schedule is exhausted without recovering. Consumers use this to surface loading/retry UI.
    /// </summary>
    event ConnectionStateChangedHandler? ConnectionStateChanged;

    /// <summary>
    /// Raised after group membership has been re-established following a reconnect or a
    /// full close-and-reinit, with the game id (if any) that was rejoined. Consumers use
    /// this as the trigger to call the state resync endpoint.
    /// </summary>
    event Func<string?, Task>? Rejoined;

    Task InitAsync(string playerId);

    /// <summary>
    /// Sets the game/lobby id to rejoin on reconnect. Called by consumers (typically from
    /// localStorage-restored state) so a reconnect or full re-init knows what to rejoin.
    /// </summary>
    void SetCurrentIds(string? gameId, string? lobbyId);
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
    // The default WithAutomaticReconnect() schedule spans ~30s, which is shorter than a cold
    // start plus image pull — the connection would die permanently before the server is up.
    private static readonly TimeSpan[] RetrySchedule =
    [
        TimeSpan.FromSeconds(0),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
    ];

    private HubConnection? _hubConnection;
    private string? _playerId;
    private string? _currentGameId;
    private string? _currentLobbyId;

    private readonly IConfiguration _configuration;
    private readonly IWebAssemblyHostEnvironment _hostEnvironment;
    private readonly ILogger<SignalRNotificationService> _logger;

    public event PokerAttackNotificationHandler? HandleNotificationReceived;
    public event ConnectionStateChangedHandler? ConnectionStateChanged;
    public event Func<string?, Task>? Rejoined;

    public SignalRNotificationService(
        IConfiguration configuration,
        IWebAssemblyHostEnvironment hostEnvironment,
        ILogger<SignalRNotificationService> logger)
    {
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public void SetCurrentIds(string? gameId, string? lobbyId)
    {
        _currentGameId = gameId;
        _currentLobbyId = lobbyId;
    }

    public async Task InitAsync(string playerId)
    {
        _playerId = playerId;

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
                    .WithAutomaticReconnect(RetrySchedule)
                    .ConfigureLogging(logging => {
                        logging.SetMinimumLevel(LogLevel.Debug);
                    })
                    .Build();

                _logger.LogInformation("Connecting to SignalR hub: {host}", baseUri.Host);

                _hubConnection.On<PokerAttackNotification>("ReceivePokerAttackNotification", notification =>
                {
                    HandleNotificationReceived?.Invoke(notification);
                });

                _hubConnection.Reconnecting += OnReconnecting;
                _hubConnection.Reconnected += OnReconnected;
                _hubConnection.Closed += OnClosed;

                await _hubConnection.StartAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing SignalR Notification Hub");
            _hubConnection = null;
        }
    }

    private Task OnReconnecting(Exception? error)
    {
        _logger.LogWarning(error, "SignalR connection lost, attempting to reconnect");
        return ConnectionStateChanged?.Invoke(true, false) ?? Task.CompletedTask;
    }

    private async Task OnReconnected(string? connectionId)
    {
        _logger.LogInformation("SignalR reconnected with connection id {connectionId}", connectionId);
        await RejoinGroupsAsync();
        if (ConnectionStateChanged is not null)
            await ConnectionStateChanged.Invoke(false, false);
    }

    /// <summary>
    /// Reconnected never fires when the connection fully closes rather than reconnecting —
    /// this is the fallback that re-runs InitAsync and then follows the same rejoin sequence.
    /// </summary>
    private async Task OnClosed(Exception? error)
    {
        _logger.LogWarning(error, "SignalR connection closed");

        if (RetrySchedule.Length == 0 || _playerId is null)
        {
            if (ConnectionStateChanged is not null)
                await ConnectionStateChanged.Invoke(false, true);
            return;
        }

        foreach (var delay in RetrySchedule)
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);

            try
            {
                await InitAsync(_playerId);
                if (_hubConnection?.State == HubConnectionState.Connected)
                {
                    await RejoinGroupsAsync();
                    if (ConnectionStateChanged is not null)
                        await ConnectionStateChanged.Invoke(false, false);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnect attempt after Closed failed");
            }
        }

        _logger.LogError("SignalR retry schedule exhausted; connection could not be re-established");
        if (ConnectionStateChanged is not null)
            await ConnectionStateChanged.Invoke(false, true);
    }

    private async Task RejoinGroupsAsync()
    {
        if (_currentGameId is string gameId)
            await JoinGameGroupAsync(gameId);
        else if (_currentLobbyId is string lobbyId)
            await JoinLobbyGroupAsync(lobbyId);

        if (Rejoined is not null)
            await Rejoined.Invoke(_currentGameId);
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
            _hubConnection.Reconnecting -= OnReconnecting;
            _hubConnection.Reconnected -= OnReconnected;
            _hubConnection.Closed -= OnClosed;
            _ = _hubConnection.StopAsync();
            _hubConnection = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing SignalR connection");
            throw;
        }
    }

    private Task<bool> EnsureConnectedAsync(string operationName)
    {
        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
            return Task.FromResult(true);

        _logger.LogWarning(
            "SignalR operation '{operation}' attempted but connection not established. State: {state}. Automatic reconnect will handle this.",
            operationName,
            _hubConnection?.State.ToString() ?? "null");

        return Task.FromResult(false);
    }

    #region Lobby Notifications
    public async Task JoinLobbyGroupAsync(string lobbyId)
    {
        try
        {
            if (!await EnsureConnectedAsync(nameof(JoinLobbyGroupAsync)))
                return; // Log and return gracefully - let automatic reconnect handle it

            await _hubConnection!.InvokeAsync("JoinLobbyGroupAsync", lobbyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining lobby group {lobbyId}", lobbyId);
            // Don't rethrow - log and continue
        }
    }

    public async Task LeaveLobbyGroupAsync(string lobbyId)
    {
        try
        {
            if (!await EnsureConnectedAsync(nameof(LeaveLobbyGroupAsync)))
                return;

            await _hubConnection!.InvokeAsync("LeaveLobbyGroupAsync", lobbyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving lobby group {lobbyId}", lobbyId);
        }
    }
    #endregion

    #region Gameplay Notifications
    public async Task JoinGameGroupAsync(string gameId)
    {
        try
        {
            if (!await EnsureConnectedAsync(nameof(JoinGameGroupAsync)))
                return; // Log and return gracefully - let automatic reconnect handle it

            await _hubConnection!.InvokeAsync("JoinGameGroupAsync", gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining game group {gameId}", gameId);
            // Don't rethrow - log and continue
        }
    }

    public async Task LeaveGameGroupAsync(string gameId)
    {
        try
        {
            if (!await EnsureConnectedAsync(nameof(LeaveGameGroupAsync)))
                return;

            await _hubConnection!.InvokeAsync("LeaveGameGroupAsync", gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving game group {gameId}", gameId);
        }
    }

    public async Task StartGameAsync(string gameId, string playerId)
    {
        try
        {
            if (!await EnsureConnectedAsync(nameof(StartGameAsync)))
                return;

            await _hubConnection!.InvokeAsync("StartGame", gameId, playerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting game {gameId}", gameId);
        }
    }

    public async Task StartRoundAsync(string gameId, string playerId)
    {
        try
        {
            if (!await EnsureConnectedAsync(nameof(StartRoundAsync)))
                return;

            await _hubConnection!.InvokeAsync("StartRound", gameId, playerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting round for game {gameId}", gameId);
        }
    }

    public async Task PlayHandAsync(string playerId, List<CardDTO> hand)
    {
        try
        {
            if (!await EnsureConnectedAsync(nameof(PlayHandAsync)))
                return;

            await _hubConnection!.InvokeAsync("PlayHand", playerId, hand);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing hand for player {playerId}", playerId);
        }
    }

    public async Task DiscardAsync(string playerId, List<CardDTO> discardCards)
    {
        try
        {
            if (!await EnsureConnectedAsync(nameof(DiscardAsync)))
                return;

            await _hubConnection!.InvokeAsync("Discard", playerId, discardCards);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discarding cards for player {playerId}", playerId);
        }
    }

    public async Task ActivatePlayerPowerAsync(string gameId, string playerId)
    {
        try
        {
            if (!await EnsureConnectedAsync(nameof(ActivatePlayerPowerAsync)))
                return;

            await _hubConnection!.InvokeAsync("ActivatePlayerPower", gameId, playerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating player power for player {playerId} in game {gameId}", playerId, gameId);
        }
    }
    #endregion

    public async Task TransitionGameStateAsync(string gameId, GameEvents gameEvent)
    {
        try
        {
            if (!await EnsureConnectedAsync(nameof(TransitionGameStateAsync)))
                return;

            await _hubConnection!.InvokeAsync("TransitionGameState", gameId, gameEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transitioning game state for game {gameId} with event {gameEvent}", gameId, gameEvent);
        }
    }

    public async Task EndGameAsync(string gameId)
    {
        try
        {
            if (!await EnsureConnectedAsync(nameof(EndGameAsync)))
                return;

            await _hubConnection!.InvokeAsync("EndGame", gameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending game {gameId}", gameId);
        }
    }

    public async Task LeaveGameAsync(string gameId, string playerId)
    {
        try
        {
            if (!await EnsureConnectedAsync(nameof(LeaveGameAsync)))
                return;

            await _hubConnection!.InvokeAsync("LeaveGame", gameId, playerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving game {gameId} for player {playerId}", gameId, playerId);
        }
    }
}
