using CommunityToolkit.Mvvm.ComponentModel;
using ChefKnifeStudios.PokerAttack.Shared;
using System.Diagnostics.SymbolStore;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IShopViewModel : IViewModel
{
    int ShopTimeSeconds { get; } 
}

public partial class ShopViewModel : BaseViewModel, IShopViewModel, IDisposable
{
    [ObservableProperty]
    int _shopTimeSeconds = PokerAttack.Shared.Constants.ShopTimeMs / 1000;

    CancellationTokenSource _timerToken;
    public ShopViewModel()
    {
        ShopTimeSeconds = PokerAttack.Shared.Constants.ShopTimeMs / 1000;
        _timerToken = TimerHelper.SetInterval(() =>
        {
            if (ShopTimeSeconds > 0) ShopTimeSeconds--;
        }, 1000);
    }

    public void Dispose()
    {
        _timerToken.Cancel();
        _timerToken.Dispose();
    }
}
