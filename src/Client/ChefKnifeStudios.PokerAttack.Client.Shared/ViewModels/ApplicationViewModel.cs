using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NameGenerator.Generators;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IApplicationViewModel : IViewModel
{
    PlayerDTO Player { get; }
    Task InitAsync();
}

public partial class ApplicationViewModel : BaseViewModel, IApplicationViewModel, IDisposable
{
    readonly ISignalRNotificationService _signalRNotificationService;
    readonly ILobbyJsInterop _lobbyJsInterop;
    readonly ILogger<ApplicationViewModel> _logger;
    readonly IConfiguration _configuration;
    readonly IWebAssemblyHostEnvironment _hostEnvironment;
    readonly IToastService _toastService;

    [ObservableProperty]
    PlayerDTO _player = new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = string.Empty,
    };

    public ApplicationViewModel(
        ISignalRNotificationService signalRNotificationService,
        ILobbyJsInterop lobbyJsInterop,
        ILogger<ApplicationViewModel> logger,
        IConfiguration configuration,
        IWebAssemblyHostEnvironment hostEnvironment,
        IToastService toastService)
    {
        _signalRNotificationService = signalRNotificationService;
        _lobbyJsInterop = lobbyJsInterop;
        _logger = logger;
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _toastService = toastService;

        var generator = new GamerTagGenerator();
        Player.Name = generator.Generate();

        _signalRNotificationService.HandleNotificationReceived += HandleSignalRNotificationReceived;
    }

    public void Dispose()
    {
        _signalRNotificationService.HandleNotificationReceived -= HandleSignalRNotificationReceived;
    }

    public async Task InitAsync()
    { 
        try
        {
            await _signalRNotificationService.InitAsync(Player.Id);

            _signalRNotificationService.HandleNotificationReceived += async (notification) =>
            {
                await Task.Run(() =>
                {
                    Console.WriteLine($"{notification.NotificationType}: {notification.Payload}");
                });
            };


            await RegisterBrowserCloseEventAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    async Task RegisterBrowserCloseEventAsync()
    {
        try
        {
            var apis = _configuration.GetSection("AppSettings:ExternalApis");
            var itemArray = apis.GetChildren();

            var setting = itemArray.FirstOrDefault(a =>
                a.GetValue<string>("Name") == nameof(APIs.PokerAttackAPI));

            if (setting != null)
            {
                var baseUrl = setting.GetValue("BaseUri", string.Empty)?.TrimEnd('/');
                if (baseUrl is null)
                {
                    string errMsg = "BaseUrl for PokerAttack API config is null.";
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

                var url = $"{baseUri.ToString().TrimEnd('/')}{PokerAttackApiEndpoints.Lobby.RemovePlayer}";

                await _lobbyJsInterop.RegisterNotifyServerOnUnloadAsync(url, new RemovePlayerReqDTO(null, new() { Id = Player.Id, Name = Player.Name, }));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering browser close event");
        }
    }

    Task HandleSignalRNotificationReceived(PokerAttackNotification notification)
    {
        switch (notification.NotificationType)
        {
            case PokerAttackNotificationType.MessageSent:
                {
                    var args = JsonSerializer.Deserialize<MessageDTO>(notification.Payload!, JsonOptions.Get());
                    if (args is MessageDTO messageDTO)
                    {
                        switch (messageDTO)
                        {
                            case { Type: MessageDTO.MessageType.Success, Title: string }:
                                _toastService.ShowSuccess(messageDTO.Title, messageDTO.Message);
                                break;
                            case { Type: MessageDTO.MessageType.Success, Title: not string }:
                                _toastService.ShowSuccess(messageDTO.Message);
                                break;
                            case { Type: MessageDTO.MessageType.Warning, Title: string }:
                                _toastService.ShowWarning(messageDTO.Title, messageDTO.Message);
                                break;
                            case { Type: MessageDTO.MessageType.Warning, Title: not string }:
                                _toastService.ShowWarning(messageDTO.Message);
                                break;
                            case { Type: MessageDTO.MessageType.Error, Title: string }:
                                _toastService.ShowError(messageDTO.Title, messageDTO.Message);
                                break;
                            case { Type: MessageDTO.MessageType.Error, Title: not string }:
                                _toastService.ShowError(messageDTO.Message);
                                break;
                        }
                    }
                    break;
                }
        }
        return Task.CompletedTask;
    }
}
