using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp;

public partial class App : ComponentBase
{
    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;
    [Inject] ISettingsService SettingsService { get; set; } = null!;
    [Inject] ICommonJsInterop CommonJsInterop { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await ApplicationViewModel.InitAsync();

        // Apply saved theme preference
        var settings = SettingsService.GetSettings();
        var themeName = settings.IsDarkModeEnabled ? "dark" : "light";
        await CommonJsInterop.SetThemeAsync(themeName);
    }
}
