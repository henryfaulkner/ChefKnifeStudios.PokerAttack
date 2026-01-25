using ChefKnifeStudios.PokerAttack.Client.Core.Services;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs.ModalEvents;

public class ModalEventArgs : IEventArgs
{
    public enum ModalActions
    {
        Open,
        Close,
    }

    public required ModalActions ModalAction { get; init; }
}
