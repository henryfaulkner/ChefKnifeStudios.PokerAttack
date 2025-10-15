using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public partial class ScoreboardListItem : ObservableObject
{
    public ScoreboardListItem(RoundDTO.RoundScoreDTO roundScore)
    {
        ClientUserId = roundScore.ClientUserId;
        ClientUserDisplayName = roundScore.ClientUserDisplayName;
        Score = roundScore.Score;
        IsEliminating = false;
        IsEliminated = false;

    }

    [ObservableProperty]
    public string _clientUserId = string.Empty;
    [ObservableProperty]
    public string _clientUserDisplayName = string.Empty;
    [ObservableProperty]
    public int _score;
    [ObservableProperty]
    public bool _isEliminating;
    [ObservableProperty]
    public bool _isEliminated;
}

public interface IScoreboardViewModel : IViewModel
{
    bool IsLoading { get; }
    ObservableCollection<ScoreboardListItem> Items { get; }
    Task LoadLatestRoundAsync(string lobbyId, CancellationToken cancellationToken = default);
    Task StartEliminatingAsync(CancellationToken cancellationToken = default);
    Task FinishEliminatingAsync(CancellationToken cancellationToken = default);
}

public partial class ScoreboardViewModel(
    IGameplayEndpointsService gameplayEndpointsService) : BaseViewModel, IScoreboardViewModel
{
    [ObservableProperty]
    bool _isLoading;

    [ObservableProperty]
    ObservableCollection<ScoreboardListItem> _items = [];

    public async Task LoadLatestRoundAsync(string lobbyId, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        var round = (await gameplayEndpointsService.GetLatestRoundAsync(lobbyId)).Value;
        Items = round?.Scores.Select(x => new ScoreboardListItem(x)).ToObservableCollection() ?? [];
        IsLoading = false;
    }

    // Mark all ScoreboardListItem in Items as IsEliminating if their score is in the bottom 25% by score.
    // If no item is included bottom 25% by score, mark the lowest scoring item as IsEliminating.
    public async Task StartEliminatingAsync(CancellationToken cancellationToken = default)
    {
        if (Items == null || Items.Count == 0)
            return;

        // Order by score (ascending: lowest first)
        var orderedByScore = Items
            .Where(i => !i.IsEliminated) // ignore already eliminated players
            .OrderBy(i => i.Score)
            .ToList();

        if (orderedByScore.Count == 0)
            return;

        // Determine bottom 25%
        int bottomCount = (int)Math.Floor(orderedByScore.Count * 0.25);
        if (bottomCount == 0)
            bottomCount = 1;

        // Get the cutoff score
        int cutoffScore = orderedByScore[bottomCount - 1].Score;

        // Include ALL players <= cutoffScore (handles tie)
        var toEliminate = orderedByScore
            .Where(x => x.Score <= cutoffScore)
            .ToList();

        // Mark items for elimination
        foreach (var item in Items)
            item.IsEliminating = toEliminate.Contains(item);

        await Task.CompletedTask;
    }

    // Mark all ScoreboardListItem in Item as IsEliminated if they are marked as IsEliminating.
    // Unmark all items marked as IsEliminated to IsEliminated as false.
    public async Task FinishEliminatingAsync(CancellationToken cancellationToken = default)
    {
        if (Items == null || Items.Count == 0)
            return;

        foreach (var item in Items)
        {
            if (item.IsEliminating)
                item.IsEliminated = true;

            item.IsEliminating = false;
        }

        await Task.CompletedTask;
    }
}
