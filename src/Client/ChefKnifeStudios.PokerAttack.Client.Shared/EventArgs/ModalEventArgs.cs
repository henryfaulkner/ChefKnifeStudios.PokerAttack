using ChefKnifeStudios.PokerAttack.Client.Core.Services;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;

public class ModalEventArgs : IEventArgs
{
    public enum ModalActions
    {
        Open,
        Close,
        CloseAll,
    }

    public required ModalActions ModalAction { get; init; }
}
