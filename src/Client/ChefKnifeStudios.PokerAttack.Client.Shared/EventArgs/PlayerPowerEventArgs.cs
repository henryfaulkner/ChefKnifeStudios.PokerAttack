using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;

public class PlayerPowerEventArgs : IEventArgs
{
    public enum EventTypes
    {
        Selection,
    }

    public class EventData
    {
        public required PlayerPowerDTO PlayerPower { get; init; }
    }

    public EventTypes Type { get; init; }
    public EventData? Data { get; init; }
}
