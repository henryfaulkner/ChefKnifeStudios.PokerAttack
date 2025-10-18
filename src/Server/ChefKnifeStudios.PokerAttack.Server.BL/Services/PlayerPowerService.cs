using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Infrastructure.PlayerPowers;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IPlayerPowerService
{
    IEnumerable<PlayerPowerDTO> GetSomePowers(int count);
    Task SelectPowerAsync(string playerId, string powerId, CancellationToken ct = default);
    void Activate(GamePlayer source, GamePlayer? target = null);
}

public class PlayerPowerService(
    ILogger<PlayerPowerService> logger,
    IPlayerPowerEffectRegistry effectRegistry,
    IPlayerPowerRepository powerRepository,
    IGamePlayerRepository gamePlayerRepository) : IPlayerPowerService
{
    public IEnumerable<PlayerPowerDTO> GetSomePowers(int count)
    {
        return powerRepository.GetRandomNumber(count).Select(x => x.MapToDTO());
    }

    public async Task SelectPowerAsync(string playerId, string powerId, CancellationToken ct = default)
    {
        var gamePlayer = await gamePlayerRepository.GetAsync(playerId, ct)
            ?? throw new KeyNotFoundException("Game Player not found");

        var playerPower = powerRepository.Get(powerId);
        gamePlayer.PlayerPower = playerPower;
        await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);
    }

    public void Activate(GamePlayer sourcePlayer, GamePlayer? targetPlayer = null)
    {
        var power = sourcePlayer.PlayerPower;

        if (power == null)
        {
            logger.LogWarning("Player tried to activate a power but has none equipped.");
            return;
        }

        // Prevent manual activation of passive powers
        if (power.PowerKind == PowerKind.Passive)
        {
            logger.LogWarning("Passive power '{PowerName}' cannot be activated manually.", power.Name);
            return;
        }

        // Check power points/energy
        if (sourcePlayer.PowerPoints < power.PointCost)
        {
            logger.LogDebug("Player does not have enough PP to activate {PowerName}.",
                power.Name);
            return;
        }

        // Deduct power points
        sourcePlayer.PowerPoints -= power.PointCost;

        // Execute each effect
        foreach (var effectInstance in power.Effects)
        {
            // Skip passive-only triggered effects
            if (effectInstance.Trigger != PowerTrigger.None)
                continue;

            // Determine target
            GamePlayer actualTarget = effectInstance.Target switch
            {
                PowerTarget.Self => sourcePlayer,
                PowerTarget.Opponent => targetPlayer ??
                    throw new InvalidOperationException($"Power {power.Name} requires a target player."),
                _ => sourcePlayer // Default to self if unspecified
            };

            try
            {
                effectRegistry.Get(effectInstance.Type).Apply(sourcePlayer, actualTarget, effectInstance.Parameters);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error applying effect {EffectType} for power {PowerName}.",
                    effectInstance?.Type, power.Name);
            }
        }
    }
}
