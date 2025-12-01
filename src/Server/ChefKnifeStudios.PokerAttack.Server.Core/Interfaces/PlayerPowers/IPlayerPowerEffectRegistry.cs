using ChefKnifeStudios.PokerAttack.Server.Core.Models;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure.PlayerPowers;

public interface IPlayerPowerEffectRegistry
{
    IPlayerPowerEffect Get(string effectType);
    void Register(string effectType, IPlayerPowerEffect effect);
}
