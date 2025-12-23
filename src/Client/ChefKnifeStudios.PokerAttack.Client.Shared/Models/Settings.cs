using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Models;

public partial class Settings : ObservableObject
{
    [ObservableProperty]
    [property: Description("Developer Mode")]
    bool _isDevModeEnabled = false;

    [ObservableProperty]
    [property: Description("Music")]
    bool _isMusicEnabled = false;

    [ObservableProperty]
    [property: Description("Sound Effects")]
    bool _isSoundEffectsEnabled = false;
}
