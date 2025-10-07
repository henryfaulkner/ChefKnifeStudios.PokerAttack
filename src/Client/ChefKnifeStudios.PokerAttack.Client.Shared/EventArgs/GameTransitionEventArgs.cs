using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;

public class GameTransitionEventArgs : IEventArgs
{
    public class EventData
    {
        public required GameEvents GameEvent { get; init; }
    }

    public required EventData Data { get; init; }
}
