using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Gameplay;

public partial class ScoreboardList : ComponentBase, IDisposable
{
    [Parameter] public required string GameId { get; set; } = null!;
    [CascadingParameter] public IGameStateMachineViewModel GameStateMachineViewModel { get; set; } = null!;
    [Inject] IScoreboardViewModel ScoreboardViewModel { get; set; } = null!;

    readonly string[] _subscriptions =
    [
        nameof(IScoreboardViewModel.IsLoading),
        nameof(IScoreboardViewModel.Items),
        nameof(IGameStateMachineViewModel.GameState),
    ];

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ScoreboardViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        ScoreboardViewModel.Items.CollectionChanged += HandleCollectionChanged;
        foreach (var item in ScoreboardViewModel.Items)
        {
            if (item is INotifyPropertyChanged npc)
                npc.PropertyChanged += HandleItemPropertyChanged;
        }
        GameStateMachineViewModel.PropertyChanged += ViewModel_OnPropertyChanged;

        _ = ScoreboardViewModel.LoadLatestRoundAsync(GameId);
    }

    public void Dispose()
    {
        ScoreboardViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        ScoreboardViewModel.Items.CollectionChanged -= HandleCollectionChanged;
        foreach (var item in ScoreboardViewModel.Items)
        {
            if (item is INotifyPropertyChanged npc)
                npc.PropertyChanged -= HandleItemPropertyChanged;
        }
        GameStateMachineViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
    }

    void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || _subscriptions.Contains(e.PropertyName) is false) return;
        if (sender is IGameStateMachineViewModel && e.PropertyName.Equals(nameof(IGameStateMachineViewModel.GameState)))
        {
            Task.Run(async () =>
            {
                await ScoreboardViewModel.StartEliminatingAsync();
                //await Task.Delay(3000);
                await ScoreboardViewModel.FinishEliminatingAsync();
            });
        }
        Task.Run(async () => await InvokeAsync(StateHasChanged));
    }

    void HandleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // New items added?
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INotifyPropertyChanged npc)
                    npc.PropertyChanged += HandleItemPropertyChanged;
            }
        }

        // Items removed?
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INotifyPropertyChanged npc)
                    npc.PropertyChanged -= HandleItemPropertyChanged;
            }
        }

        InvokeAsync(StateHasChanged);
    }

    void HandleItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }
}
