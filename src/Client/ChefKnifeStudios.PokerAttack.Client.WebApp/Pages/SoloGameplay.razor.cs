using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp.Pages;

public partial class SoloGameplay : ComponentBase, IAsyncDisposable
{
    [Inject] ISoloGameplayViewModel SoloGameplayViewModel { get; set; } = null!;
    [Inject] IInputJsInterop InputJsInterop { get; set; } = null!;
    [Inject] IInputService InputService { get; set; } = null!;

    protected override void OnInitialized()
    {
        base.OnInitialized();

        InputService.RegisterKeyAction("q", () => Task.Run(() => HandlePlayHandPressed()));
        InputService.RegisterKeyAction("w", () => Task.Run(() => HandleDiscardPressed()));
        InputService.RegisterKeyAction("a", () => Task.Run(() => HandleSortByRankPressed()));
        InputService.RegisterKeyAction("s", () => Task.Run(() => HandleSortBySuitPressed()));
        InputService.RegisterKeyAction("d", () => Task.Run(() => HandleClearSelectionsPressed()));
        InputService.RegisterKeyAction("1", () => Task.Run(() => ToggleCardSelection(0)));
        InputService.RegisterKeyAction("2", () => Task.Run(() => ToggleCardSelection(1)));
        InputService.RegisterKeyAction("3", () => Task.Run(() => ToggleCardSelection(2)));
        InputService.RegisterKeyAction("4", () => Task.Run(() => ToggleCardSelection(3)));
        InputService.RegisterKeyAction("5", () => Task.Run(() => ToggleCardSelection(4)));
        InputService.RegisterKeyAction("6", () => Task.Run(() => ToggleCardSelection(5)));
        InputService.RegisterKeyAction("7", () => Task.Run(() => ToggleCardSelection(6)));
        InputService.RegisterKeyAction("8", () => Task.Run(() => ToggleCardSelection(7)));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await InputJsInterop.RegisterKeyHandlerAsync(async key =>
            {
                await InputService.HandleKeyAsync(key);
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        await InputJsInterop.DisposeAsync();
    }

    void HandlePlayHandPressed()
    {
        // TODO Check if Game State is InGame
        SoloGameplayViewModel.PlaySelectedCards();
    }

    void HandleDiscardPressed()
    {
        // TODO Check if Game State is InGame
        SoloGameplayViewModel.DiscardSelectedCards();
    }

    void HandleSortByRankPressed()
    {
        // TODO Check if Game State is InGame
        SoloGameplayViewModel.SortByRank();
    }

    void HandleSortBySuitPressed()
    {
        // TODO Check if Game State is InGame
        SoloGameplayViewModel.SortBySuit();
    }

    void HandleClearSelectionsPressed()
    {
        // TODO Check if Game State is InGame
        SoloGameplayViewModel.ClearSelections();
    }

    void ToggleCardSelection(int index)
    {
        // TODO Check if Game State is InGame
        SoloGameplayViewModel.ToggleCardSelection(index);
        StateHasChanged();
    }

    static string FormatAsMinutesSeconds(int totalSeconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(totalSeconds);
        return time.ToString(@"mm\:ss");
    }
}
