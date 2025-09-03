using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services;

public delegate Task PokerAttackNotificationHandler(PokerAttackNotification notification);

public interface ISignalRNotificationService
{
    Task InitAsync();
    event PokerAttackNotificationHandler? HandleNotificationReceived;

    Task BroadcastTestNotification(string gameId, string message);
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

    public async Task InitAsync()
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

                var url = $"{baseUri.ToString().TrimEnd('/')}/cks-notification";

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(url) // no authentication
                    .WithAutomaticReconnect()
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

    public void Dispose() => CloseConnection();

    private void CloseConnection()
    {
        if (_hubConnection == null) return;
        _ = _hubConnection.StopAsync();
        _hubConnection = null;
    }


    public async Task BroadcastTestNotification(string gameId, string message)
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.InvokeAsync("BroadcastGameNotification", gameId, 
                new PokerAttackNotification(PokerAttackNotificationType.PlayerJoined, message));
        }
    }
}
