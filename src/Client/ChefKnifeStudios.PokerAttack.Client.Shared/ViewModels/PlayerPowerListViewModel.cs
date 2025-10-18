using ChefKnifeStudios.PokerAttack.Client.Core.Extensions;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
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
    bool IsLoading { get; }
    ObservableCollection<PlayerPowerListItem> Items { get; }
    Task LoadItemsAsync(CancellationToken cancellationToken = default);
    Task SubmitSelectedItemAsync(CancellationToken cancellationToken = default);
}

public partial class PlayerPowerListViewModel(
    IApplicationViewModel applicationViewModel,
    IPlayerPowerEndpointsService playerPowerEndpointsService,
    IToastService toastService) : BaseViewModel, IPlayerPowerListViewModel
{
    [ObservableProperty]
    bool _isLoading;

    [ObservableProperty]
    ObservableCollection<PlayerPowerListItem> _items = [];

    public async Task LoadItemsAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        var playerPowers = (await playerPowerEndpointsService.GetSomePowersAsync()).Value;
        Items = playerPowers?.Select(x => new PlayerPowerListItem(x)).ToObservableCollection() ?? [];
        IsLoading = false;
    }

    public async Task SubmitSelectedItemAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        var selectedPower = Items.FirstOrDefault(x => x.IsSelected);
        if (selectedPower is null)
        {
            toastService.ShowWarning("Select a power");
            return;
        }

        await playerPowerEndpointsService.SelectPlayerPowerAsync(
            applicationViewModel.Player.Id,
            selectedPower.Id,
            cancellationToken
        );
        IsLoading = false;
    }
}
