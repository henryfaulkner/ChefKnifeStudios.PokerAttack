using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components;

public partial class ModalController : ComponentBase, IDisposable
{
    [Inject] IModalControllerViewModel ViewModel { get; set; } = null!;

    protected override void OnInitialized()
    {
        ViewModel.PropertyChanged += OnViewModelChanged;
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        ViewModel.PropertyChanged -= OnViewModelChanged;
    }
}
