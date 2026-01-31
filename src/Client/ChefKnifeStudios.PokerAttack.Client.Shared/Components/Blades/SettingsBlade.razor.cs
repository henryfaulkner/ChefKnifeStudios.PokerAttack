using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Blades;

public partial class SettingsBlade : ComponentBase, IDisposable
{
    [SupplyParameterFromQuery] public string? GameId { get; set; }

    [Inject] ILogger<SettingsBlade> Logger { get; set; } = null!;
    [Inject] IEventNotificationService EventNotificationService { get; set; } = null!;
    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;
    [Inject] ISettingsService SettingsService { get; set; } = null!;
    [Inject] NavigationManager NavigationManager { get; set; } = null!;
    [Inject] IGameplayEndpointsService GameplayEndpointsService { get; set; } = null!;
    [Inject] ICommonJsInterop CommonJsInterop { get; set; } = null!;

    BladeContainer? _bladeContainer;

    protected override void OnInitialized()
    {
        EventNotificationService.EventReceived += HandleEventReceived;
        base.OnInitialized();
    }

    public void Dispose()
    {
        EventNotificationService.EventReceived -= HandleEventReceived;
        GC.SuppressFinalize(this);
    }

    async Task HandleEventReceived(object sender, IEventArgs e)
    {
        switch (e)
        {
            case BladeEventArgs { Type: BladeEventArgs.Types.Settings, }:
                _bladeContainer?.Open();
                break;
            case BladeEventArgs { Type: not BladeEventArgs.Types.Settings, }:
                _bladeContainer?.Close();
                break;
            default:
                Logger.LogWarning("Event handler's switch statement fell through.");
                break;
        }
        await Task.CompletedTask;
    }

    async void HandleSettingPressed(string propertyName, bool val)
    {
        SettingsService.SetSettingValue(propertyName, val);

        // Apply theme immediately when dark mode setting changes
        if (propertyName == nameof(Settings.IsDarkModeEnabled))
        {
            var themeName = val ? "dark" : "light";
            await CommonJsInterop.SetThemeAsync(themeName);

            // Notify other components (e.g., MainLayout) to update their theme
            EventNotificationService.PostEvent(this, new ThemeChangedEventArgs { IsDarkMode = val });
        }
    }

    async Task HandleLeaveGamePressed()
    {
        try
        {
            if (NavigationManager.IsOnMultiGameplay())
            {
                // Multiplayer - call API to leave game
                if (GameId is { Length: > 0 })
                {
                    await GameplayEndpointsService.LeaveGameAsync(GameId, ApplicationViewModel.Player.Id);
                }
                else
                {
                    Logger.LogWarning("Player unable to leave multiplayer game because their GameId was null or empty.");
                }
            }
            // For both multi and solo, navigate to lobby
            NavigationManager.NavigateToLobby();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred.");
        }
    }
}
