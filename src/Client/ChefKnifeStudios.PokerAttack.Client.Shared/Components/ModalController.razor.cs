using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components;

public partial class ModalController : ComponentBase, IDisposable
{
    [Inject] IModalControllerViewModel ModalControllerViewModel { get; set; } = null!;

    protected override void OnInitialized()
    {
        ModalControllerViewModel.PropertyChanged += OnViewModelChanged;
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        ModalControllerViewModel.PropertyChanged -= OnViewModelChanged;
    }
}
