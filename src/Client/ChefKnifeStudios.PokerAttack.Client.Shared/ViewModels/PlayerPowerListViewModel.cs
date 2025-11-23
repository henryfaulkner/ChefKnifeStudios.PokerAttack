using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public partial class PlayerPowerListItem : ObservableObject
{
    public PlayerPowerListItem(PlayerPowerDTO playerPower)
    {
        Id = playerPower.Id;
        Name = playerPower.Name;
        Description = playerPower.Description;
        PointCost = playerPower.PointCost;
        IsSelected = false;
    }

    [ObservableProperty]
    public string _id = "";
    [ObservableProperty]
    public string _name = "";
    [ObservableProperty]
    public string _description = "";
    [ObservableProperty]
    public int _pointCost;
    [ObservableProperty]
    public bool _isSelected = false;
}

public interface IPlayerPowerListViewModel : IViewModel
{
    string GameId { get; }
    bool IsLoading { get; }
    ObservableCollection<PlayerPowerListItem> Items { get; }
    void Init(string gameId);
    Task LoadItemsAsync(CancellationToken cancellationToken = default);
    Task SubmitSelectedItemAsync(PlayerPowerListItem playerPower, CancellationToken cancellationToken = default);
}

public partial class PlayerPowerListViewModel(
    ILogger<PlayerPowerListViewModel> logger,
    IApplicationViewModel applicationViewModel,
    IPlayerPowerEndpointsService playerPowerEndpointsService,
    IToastService toastService,
    IEventNotificationService eventNotificationService) : BaseViewModel, IPlayerPowerListViewModel
{
    [ObservableProperty]
    string? _gameId;

    [ObservableProperty]
    bool _isLoading;

    [ObservableProperty]
    ObservableCollection<PlayerPowerListItem> _items = [];

    public void Init(string gameId)
    {
        GameId = gameId;
    }

    public async Task LoadItemsAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        var playerPowers = (await playerPowerEndpointsService.GetPlayerPowersAsync()).Value;
        Items = playerPowers?.Select(x => new PlayerPowerListItem(x)).ToObservableCollection() ?? [];
        IsLoading = false;
    }

    public async Task SubmitSelectedItemAsync(PlayerPowerListItem playerPower, CancellationToken cancellationToken = default)
    {
        if (GameId is null) throw new ApplicationException("ScoreboardViewModel must Init before loading rounds.");
        try
        {
            foreach (var pp in Items) pp.IsSelected = false;
            playerPower.IsSelected = true;
            if (playerPower is null)
            {
                toastService.ShowWarning("Select a power");
                return;
            }

            var res = await playerPowerEndpointsService.SelectPlayerPowerAsync(
                GameId,
                applicationViewModel.Player.Id,
                playerPower.Id,
                cancellationToken
            );

            if (!res.IsSuccess && res.Value is not PlayerPowerDTO)
            {
                toastService.ShowError("Player power was not selected");
                playerPower.IsSelected = false;
                return;
            }

            eventNotificationService.PostEvent(
                this,
                new PlayerPowerEventArgs
                {
                    Type = PlayerPowerEventArgs.EventTypes.Selection,
                    Data = new PlayerPowerEventArgs.EventData { PlayerPower = res.Value },
                }
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred");
        }
    }
}
