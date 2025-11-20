using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public partial class ShopItem : ObservableObject
{

}

public interface IShopViewModel : IViewModel
{
    string GameId { get; }
    bool IsLoading { get; }
    ObservableCollection<ShopItem> Items { get; }
    void Init(string gameId);
}

public partial class ShopViewModel(
    IApplicationViewModel applicationViewModel,
    IGameplayEndpointsService gameplayEndpointsService) : BaseViewModel, IShopViewModel
{
    [ObservableProperty]
    string? _gameId;

    [ObservableProperty]
    bool _isLoading = false;

    [ObservableProperty]
    ObservableCollection<ShopItem> _items = [];

    public void Init(string gameId)
    {
        GameId = gameId;
    }
}
