using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IScoringRulesViewModel : IViewModel
{
    bool IsLoading { get; }
    ScoringRules? ScoringRules { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
}

public partial class ScoringRulesViewModel(
    ILogger<ScoringRulesViewModel> logger,
    IScoringRulesEndpointsService scoringRulesEndpointsService) : BaseViewModel, IScoringRulesViewModel
{
    [ObservableProperty]
    bool _isLoading = false;

    [ObservableProperty]
    ScoringRules? _scoringRules = null;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IsLoading = true;
            var result = await scoringRulesEndpointsService.GetScoringRulesAsync(cancellationToken);
            if (result.IsSuccess && result.Value is ScoringRules scoringRules)
            {
                ScoringRules = scoringRules;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occured while loading scoring rules.");
        }
        finally
        { 
            IsLoading = false; 
        }
    }
}
