using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp.Pages;

public partial class Gameplay : ComponentBase
{
    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;
    [Inject] IGameplayViewModel GameplayViewModel { get; set; } = null!;

    readonly string[] _subscriptions =
    [
        nameof(IGameplayViewModel.Score),
        nameof(IGameplayViewModel.CardsInHand),
    ];

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await GameplayViewModel.StartRunAsync(ApplicationViewModel.Player.Id);


        GameplayViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        GameplayViewModel.CardsInHand.CollectionChanged += CardsInHand_CollectionChanged;
    }

    public void Dispose()
    {
        GameplayViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        GameplayViewModel.CardsInHand.CollectionChanged -= CardsInHand_CollectionChanged;
    }

    private void CardsInHand_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }

    void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_subscriptions.Contains(e.PropertyName) is false) return;
        Task.Run(async () => await InvokeAsync(StateHasChanged));
    }
}
