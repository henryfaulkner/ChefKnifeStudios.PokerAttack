using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Models;

public partial class Settings : ObservableObject
{
    [ObservableProperty]
    [property: Description("Audio Enabled")]
    bool _isAudioEnabled = true;

    [ObservableProperty]
    [property: Description("Always Show App Tour")]
    bool _isAlwaysShowAppTour = false;
}
