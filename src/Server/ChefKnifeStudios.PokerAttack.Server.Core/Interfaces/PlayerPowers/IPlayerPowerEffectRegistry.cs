using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure.PlayerPowers;

public interface IPlayerPowerEffectRegistry
{
    Result<IPlayerPowerEffect> Get(string effectType);
    void Register(string effectType, IPlayerPowerEffect effect);
}
