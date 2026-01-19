using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Lobbies;

public partial class SoloGameResultModal : ComponentBase, IDisposable
{
    [Inject] ISoloGameResultStore SoloGameResultStore { get; set; } = null!;

    protected override void OnInitialized()
    {
        SoloGameResultStore.PropertyChanged += Store_OnPropertyChanged;
        base.OnInitialized();
    }

    public void Dispose()
    {
        SoloGameResultStore.PropertyChanged -= Store_OnPropertyChanged;
        GC.SuppressFinalize(this);
    }

    void Store_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }

    void HandleClosePressed()
    {
        SoloGameResultStore.Clear();
    }
}
