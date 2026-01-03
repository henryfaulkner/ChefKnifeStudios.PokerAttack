using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure.PlayerPowers;

public class PlayerPowerEffectRegistry : IPlayerPowerEffectRegistry
{
    readonly Dictionary<string, IPlayerPowerEffect> _effects = new();

    public PlayerPowerEffectRegistry()
    {
        Register("unflipAllCards", new UnflipAllCardsEffect());
        Register("flipRandomCards", new FlipRandomCardsEffect());
        Register("changeRandomCardsToRandomSuit", new ChangeRandomCardsToRandomSuitEffect());
        Register("changeRandomCardsToRandomRank", new ChangeRandomCardsToRandomRankEffect());
        Register("changeSelectedCardsToSuit", new ChangeSelectedCardsToSuitEffect());
    }

    public Result<IPlayerPowerEffect> Get(string effectType)
    {
        if (!_effects.TryGetValue(effectType, out var effect))
            return Result.NotFound($"Effect type '{effectType}' not registered.");
        return Result.Success(effect);
    }

    public void Register(string effectType, IPlayerPowerEffect effect)
    {
        _effects[effectType] = effect;
    }
}
