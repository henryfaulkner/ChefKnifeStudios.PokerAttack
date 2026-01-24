using CommunityToolkit.Mvvm.ComponentModel;
using ChefKnifeStudios.PokerAttack.Shared;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IShopViewModel : IViewModel
{
    int ShopTimeSeconds { get; }
}

public partial class ShopViewModel : BaseViewModel, IShopViewModel, IDisposable
{
    readonly IApplicationViewModel _applicationViewModel;

    [ObservableProperty]
    int _shopTimeSeconds;

    CancellationTokenSource? _timerToken;

    public ShopViewModel(IApplicationViewModel applicationViewModel)
    {
        _applicationViewModel = applicationViewModel;
        ShopTimeSeconds = _applicationViewModel.GameSettings.ShopTimeMs / 1000;
        StartTimer();
    }

    public void Dispose()
    {
        _timerToken?.Cancel();
        _timerToken?.Dispose();
    }

    void StartTimer()
    {
        // Cancel any existing timer
        _timerToken?.Cancel();
        _timerToken?.Dispose();
        _timerToken = null;

        _timerToken = TimerHelper.SetInterval(() =>
        {
            if (ShopTimeSeconds > 0) ShopTimeSeconds--;
        }, 1000);
    }
}
