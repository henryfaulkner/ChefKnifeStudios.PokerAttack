using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR.EventArgs;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.AspNetCore.Components;
using System.Collections.ObjectModel;
using System.Text.Json;

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
    string GameId { get; }
    bool IsLoading { get; }
    ObservableCollection<ScoreboardListItem> Items { get; }
    void Init(string gameId);
    Task LoadLatestRoundAsync(CancellationToken cancellationToken = default);
}

public partial class ScoreboardViewModel : BaseViewModel, IScoreboardViewModel, IDisposable
{
    readonly IApplicationViewModel _applicationViewModel;
    readonly IGameplayEndpointsService _gameplayEndpointsService;
    readonly ISignalRNotificationService _signalRNotificationService;

    [ObservableProperty]
    string? _gameId;

    [ObservableProperty]
    bool _isLoading;

    [ObservableProperty]
    ObservableCollection<ScoreboardListItem> _items = [];

    public ScoreboardViewModel(
        IApplicationViewModel applicationViewModel,
        IGameplayEndpointsService gameplayEndpointsService,
        NavigationManager navigationManager,
        ISignalRNotificationService signalRNotificationService)
    {
        _applicationViewModel = applicationViewModel;
        _gameplayEndpointsService = gameplayEndpointsService;
        _signalRNotificationService = signalRNotificationService;

        _signalRNotificationService.HandleNotificationReceived += HandleSignalRNotificationReceived;
    }

    public void Dispose()
    {
        _signalRNotificationService.HandleNotificationReceived -= HandleSignalRNotificationReceived;
    }

    public void Init(string gameId)
    {
        GameId = gameId;
    }

    public async Task LoadLatestRoundAsync(CancellationToken cancellationToken = default)
    {
        if (GameId is null) throw new ApplicationException("ScoreboardViewModel must Init before loading rounds.");
        IsLoading = true;
        var round = (await _gameplayEndpointsService.GetLatestRoundAsync(GameId, cancellationToken)).Value;
        Items = round?.Scores.Select(x => new ScoreboardListItem(x)).ToObservableCollection() ?? [];
        IsLoading = false;
    }

    Task HandleSignalRNotificationReceived(PokerAttackNotification notification)
    {
        switch (notification.NotificationType)
        {
            case PokerAttackNotificationType.EliminationStarted:
                {
                    if (string.IsNullOrWhiteSpace(notification.Payload))
                        break;

                    var args = JsonSerializer.Deserialize<EliminationStartedArgs>(notification.Payload!);
                    if (args?.Players == null)
                        break;

                    var ids = args.Players.Select(p => p.PlayerId).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // Mark items that are starting elimination
                    foreach (var item in Items)
                    {
                        item.IsEliminating = ids.Contains(item.ClientUserId);
                    }
                    break;
                }
            case PokerAttackNotificationType.EliminationFinished:
                {
                    if (string.IsNullOrWhiteSpace(notification.Payload))
                        break;

                    var args = JsonSerializer.Deserialize<EliminationFinishedArgs>(notification.Payload!);
                    if (args?.Losers == null)
                        break;
                    break;
                }
        }
        return Task.CompletedTask;
    }
}
