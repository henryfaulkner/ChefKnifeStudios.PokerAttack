using CommunityToolkit.Mvvm.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Models;

public partial class Settings : ObservableObject
{
    [ObservableProperty]
    public bool _isDevModeEnabled = false;
}
