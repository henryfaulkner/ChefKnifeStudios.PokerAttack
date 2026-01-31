using ChefKnifeStudios.PokerAttack.Client.Core.Services;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;

public class ThemeChangedEventArgs : IEventArgs
{
    public required bool IsDarkMode { get; init; }
}
