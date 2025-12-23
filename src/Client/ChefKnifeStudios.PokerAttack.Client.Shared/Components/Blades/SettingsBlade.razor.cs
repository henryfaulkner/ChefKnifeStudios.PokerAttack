using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Blades;

public partial class SettingsBlade : ComponentBase, IDisposable
{
    [Inject] ILogger<SettingsBlade> Logger { get; set; } = null!;
    [Inject] IEventNotificationService EventNotificationService { get; set; } = null!;
    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;

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

    void HandleSettingPressed(string propertyName, bool val)
    {
        var type = ApplicationViewModel.Settings.GetType();
        var properties = type.GetProperties();
        foreach (var property in properties)
        {
            if (property.Name == propertyName)
            {
                property.SetValue(ApplicationViewModel.Settings, val);
            }
        }
    }
}
