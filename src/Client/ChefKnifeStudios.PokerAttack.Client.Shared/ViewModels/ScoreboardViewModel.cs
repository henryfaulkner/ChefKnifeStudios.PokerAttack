using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IScoreboardViewModel : IViewModel
{
    bool IsLoading { get; }
    RoundDTO Round { get; }
    Task LoadLatestRoundAsync(string lobbyId, CancellationToken cancellationToken = default);
}

public partial class ScoreboardViewModel(
    IGameplayEndpointsService gameplayEndpointsService) : BaseViewModel, IScoreboardViewModel
{
    [ObservableProperty]
    bool _isLoading;

    [ObservableProperty]
    RoundDTO? _round;

    public async Task LoadLatestRoundAsync(string lobbyId, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        Round = await gameplayEndpointsService.GetLatestRoundAsync(lobbyId);
        IsLoading = false;
    }
}
