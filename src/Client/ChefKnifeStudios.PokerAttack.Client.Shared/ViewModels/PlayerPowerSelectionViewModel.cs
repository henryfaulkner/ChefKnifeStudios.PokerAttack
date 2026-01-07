using CommunityToolkit.Mvvm.ComponentModel;
using ChefKnifeStudios.PokerAttack.Shared;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IPlayerPowerSelectionViewModel : IViewModel
{
    int SelectionTimeSeconds { get; }
}

public partial class PlayerPowerSelectionViewModel : BaseViewModel, IPlayerPowerSelectionViewModel, IDisposable
{
    [ObservableProperty]
    int _selectionTimeSeconds = PokerAttack.Shared.Constants.PlayerPowerSelectionTimeMs / 1000;

    CancellationTokenSource _timerToken;

    public PlayerPowerSelectionViewModel()
    {
        SelectionTimeSeconds = PokerAttack.Shared.Constants.PlayerPowerSelectionTimeMs / 1000;
        _timerToken = TimerHelper.SetInterval(() =>
        {
            if (SelectionTimeSeconds > 0) SelectionTimeSeconds--;
        }, 1000);
    }

    public void Dispose()
    {
        _timerToken.Cancel();
        _timerToken.Dispose();
    }
}
