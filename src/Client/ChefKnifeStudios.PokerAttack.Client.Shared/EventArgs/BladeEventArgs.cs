using ChefKnifeStudios.PokerAttack.Client.Core.Services;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;

public class BladeEventArgs : IEventArgs
{
    public enum Types
    {
        Close,
    }

    public required Types Type { get; init; }

    public object? Data { get; init; }
}
