using ChefKnifeStudios.PokerAttack.Client.Core.Enums;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IApplicationViewModel : IViewModel
{
    string PlayerId { get; }
    Task InitAsync();
}

public partial class ApplicationViewModel : BaseViewModel, IApplicationViewModel
{
    readonly ISignalRNotificationService _signalRNotificationService;
    readonly ILobbyJsInterop _lobbyJsInterop;
    readonly ILogger<ApplicationViewModel> _logger;
    readonly IConfiguration _configuration;
    readonly IWebAssemblyHostEnvironment _hostEnvironment;

    [ObservableProperty]
    string _playerId = Guid.NewGuid().ToString();

    public ApplicationViewModel(
        ISignalRNotificationService signalRNotificationService,
        ILobbyJsInterop lobbyJsInterop,
        ILogger<ApplicationViewModel> logger,
        IConfiguration configuration)
    {
        _signalRNotificationService = signalRNotificationService;
        _lobbyJsInterop = lobbyJsInterop;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InitAsync()
    { 
        try
        {
            await _signalRNotificationService.InitAsync();

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

                await _lobbyJsInterop.RegisterNotifyServerOnUnloadAsync(url, new RemovePlayerReqDTO(null, PlayerId));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering browser close event");
        }
    }
}
