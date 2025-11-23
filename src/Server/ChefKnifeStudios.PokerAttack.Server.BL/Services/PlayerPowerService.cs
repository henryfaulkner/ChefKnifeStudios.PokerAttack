using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Infrastructure.PlayerPowers;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IPlayerPowerService
{
    IEnumerable<PlayerPowerDTO> GetPlayerPowers(int count);
    Task<PlayerPowerDTO> SelectPlayerPowerAsync(string gameId, string playerId, string powerId, CancellationToken ct = default);
    Task ActivateAsync(string gameId, string playerId, CancellationToken ct = default);
}

public class PlayerPowerService(
    ILogger<PlayerPowerService> logger,
    IPlayerPowerEffectRegistry effectRegistry,
    IPlayerPowerRepository powerRepository,
    IKeyValueRepository<GamePlayer> gamePlayerRepository,
    IKeyValueRepository<ActiveGame> activeGameRepository,
    IPokerAttackNotificationHelper notificationHelper,
    IGameStateMachineService gameStateMachineService) : IPlayerPowerService
{
    public IEnumerable<PlayerPowerDTO> GetPlayerPowers(int count)
    {
        return powerRepository.GetRandomNumber(count).Select(x => x.MapToDTO());
    }

    public async Task<PlayerPowerDTO> SelectPlayerPowerAsync(string gameId, string playerId, string powerId, CancellationToken ct = default)
    {
        var gamePlayer = await gamePlayerRepository.GetAsync(playerId, ct)
            ?? throw new KeyNotFoundException("Game Player not found");

        var playerPower = powerRepository.Get(powerId)
            ?? throw new KeyNotFoundException("Player Power not found");
        gamePlayer.PlayerPower = playerPower;
        await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);

        var activeGame = await activeGameRepository.GetAsync(gameId, ct);
        if (activeGame is ActiveGame && await DoesEveryPlayersInGameHaveAPower(activeGame))
        {
            await gameStateMachineService.TransitionAsync(gameId, GameEvents.Next, ct);
        }

        return playerPower.MapToDTO();
    }

    public async Task ActivateAsync(string gameId, string sourcePlayerId, CancellationToken ct = default)
    {
        var activeGame = await activeGameRepository.GetAsync(gameId)
            ?? throw new KeyNotFoundException("Lobby not found");
        var players = activeGame.Players.ToHashSet();

        var sourcePlayer = players.FirstOrDefault(x => x.Id == sourcePlayerId);
        players.RemoveWhere(x => x.Id == sourcePlayerId);
        var targetPlayerIfApplicable = players.Any() ? players.ToList()[Random.Shared.Next(0, players.Count())] : null;
        var targetGamePlayerIdIfApplicable = targetPlayerIfApplicable?.Id;

        var sourceGamePlayer = await gamePlayerRepository.GetAsync(sourcePlayerId)
            ?? throw new KeyNotFoundException("Source Game Player not found");
        var targetGamePlayer = targetGamePlayerIdIfApplicable is string ? await gamePlayerRepository.GetAsync(targetGamePlayerIdIfApplicable) : null;


        var power = sourceGamePlayer.PlayerPower;

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
        if (sourceGamePlayer.PowerPoints < power.PointCost)
        {
            logger.LogDebug("Player does not have enough PP to activate {PowerName}.",
                power.Name);
            return;
        }

        // Deduct power points
        sourceGamePlayer.PowerPoints -= power.PointCost;

        var selfTargeted = false;
        var selfTargetMsg = string.Empty;
        var targetTargeted = false;
        var targetTargetMsg = string.Empty;

        // Execute each effect
        foreach (var effectInstance in power.Effects)
        {
            // Skip passive-only triggered effects
            if (effectInstance.Trigger != PowerTrigger.None)
                continue;

            // Determine target
            GamePlayer actualTarget;

            switch (effectInstance.Target)
            {
                case PowerTarget.Self:
                    selfTargeted = true;
                    actualTarget = sourceGamePlayer;
                    selfTargetMsg += $"{effectInstance.PowerMessage}. ";
                    break;

                case PowerTarget.Opponent:
                    if (targetGamePlayer == null)
                        throw new InvalidOperationException($"Power {power.Name} requires a target player.");
                    targetTargeted = true;
                    targetTargetMsg += $"{effectInstance.PowerMessage}. ";
                    actualTarget = targetGamePlayer;
                    break;

                default:
                    // Default to self if unspecified
                    selfTargeted = true;
                    selfTargetMsg += $"{effectInstance.PowerMessage}. ";
                    actualTarget = sourceGamePlayer;
                    break;
            }

            try
            {
                effectRegistry.Get(effectInstance.Type).Apply(sourceGamePlayer, actualTarget, effectInstance.Parameters);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error applying effect {EffectType} for power {PowerName}.",
                    effectInstance?.Type, power.Name);
            }
        }

        if (selfTargeted)
        {
            // Deal affected cards to source player
            await notificationHelper.SendToPlayerAsync(sourcePlayerId, new PokerAttackNotification
            (
                PokerAttackNotificationType.CardsDealt,
                JsonSerializer.Serialize(sourceGamePlayer.CardsInHand.Select(x => x.MapToDTO()), JsonOptions.Get())
            ));

            // Message to source player
            await notificationHelper.SendToPlayerAsync(sourcePlayerId, new PokerAttackNotification
            (
                PokerAttackNotificationType.MessageSent,
                JsonSerializer.Serialize(new MessageDTO { Title = $"{sourcePlayer?.Name} played power", Message = selfTargetMsg, Type = MessageDTO.MessageType.Success, })
            ));
        }
        if (targetTargeted && targetGamePlayerIdIfApplicable is string && targetGamePlayer is GamePlayer)
        {
            // Deal affected cards to target player
            await notificationHelper.SendToPlayerAsync(targetGamePlayerIdIfApplicable, new PokerAttackNotification
            (
                PokerAttackNotificationType.CardsDealt,
                JsonSerializer.Serialize(targetGamePlayer.CardsInHand.Select(x => x.MapToDTO()), JsonOptions.Get())
            ));

            // Message to both players
            await notificationHelper.SendToPlayerAsync(sourcePlayerId, new PokerAttackNotification
            (
                PokerAttackNotificationType.MessageSent,
                JsonSerializer.Serialize(new MessageDTO { Title = $"{sourcePlayer?.Name} attacked {targetPlayerIfApplicable?.Name}", Message = targetTargetMsg, Type = MessageDTO.MessageType.Success, })
            ));
            await notificationHelper.SendToPlayerAsync(targetGamePlayerIdIfApplicable, new PokerAttackNotification
            (
                PokerAttackNotificationType.MessageSent,
                JsonSerializer.Serialize(new MessageDTO { Title = $"{sourcePlayer?.Name} attacked {targetPlayerIfApplicable?.Name}", Message = targetTargetMsg, Type = MessageDTO.MessageType.Success, })
            ));
        }
    }

    async Task<bool> DoesEveryPlayersInGameHaveAPower(ActiveGame activeGame, CancellationToken ct = default)
    {
        bool result = true;
        foreach (var player in activeGame.Players)
        {
            var gamePlayer = await gamePlayerRepository.GetAsync(player.Id, ct)
                ?? throw new KeyNotFoundException("Game Player not found");
            if (gamePlayer.PlayerPower is not PlayerPower) result = false;
        }
        return result;
    }
}
