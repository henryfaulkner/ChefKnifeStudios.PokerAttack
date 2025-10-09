using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using System.ComponentModel;
using System.Threading.Tasks;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Gameplay;

public partial class ScoreboardList : ComponentBase, IDisposable
{
    [Parameter] public required string GameId { get; set; } = null!;

    [Inject] IScoreboardViewModel ScoreboardViewModel { get; set; } = null!;

    readonly string[] _subscriptions =
    [
        nameof(IScoreboardViewModel.IsLoading),
        nameof(IScoreboardViewModel.Round),
    ];

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ScoreboardViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
            
        _ = ScoreboardViewModel.LoadLatestRoundAsync(GameId);
    }

    public void Dispose()
    {
        ScoreboardViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
    }

    void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_subscriptions.Contains(e.PropertyName) is false) return;
        Task.Run(async () => await InvokeAsync(StateHasChanged));
    }
}
