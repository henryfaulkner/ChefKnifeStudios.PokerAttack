using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.AspNetCore.Components;
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
    IApplicationViewModel applicationViewModel,
    IGameplayEndpointsService gameplayEndpointsService,
    NavigationManager navigationManager) : BaseViewModel, IScoreboardViewModel
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
    // Always leave at least one player uneliminated.
    public async Task StartEliminatingAsync(CancellationToken cancellationToken = default)
    {
        if (Items == null || Items.Count == 0)
            return;

        // Get non-eliminated players and order by score ascending
        var orderedByScore = Items
            .Where(i => !i.IsEliminated)
            .OrderBy(i => i.Score)
            .ToList();

        if (orderedByScore.Count == 0)
            return;

        // Determine bottom 25%
        int bottomCount = (int)Math.Floor(orderedByScore.Count * 0.25);
        if (bottomCount == 0)
            bottomCount = 1;

        // Cutoff score (handles ties)
        int cutoffScore = orderedByScore[bottomCount - 1].Score;

        // Select all players at or below cutoff
        var toEliminate = orderedByScore
            .Where(x => x.Score <= cutoffScore)
            .ToList();

        // Rule: Never eliminate everyone
        if (toEliminate.Count >= orderedByScore.Count)
        {
            // Edge case: tied elimination would remove all players
            toEliminate.Clear();
        }

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

        var me = Items.FirstOrDefault(x => x.ClientUserId == applicationViewModel.Player.Id);
        bool amILoser = Items.Any(x => x.IsEliminated && x.ClientUserId == applicationViewModel.Player.Id);
        bool amIWinner = Items.Count == 1;
        if (amILoser)
        {
            navigationManager.NavigateTo($"/game-over?result=loser&score={me?.Score ?? 0}", replace: true);
        }
        else if (amIWinner)
        {
            navigationManager.NavigateTo($"/game-over?result=winner&score={me?.Score ?? 0}", replace: true);
        }

        await Task.CompletedTask;
    }
}
