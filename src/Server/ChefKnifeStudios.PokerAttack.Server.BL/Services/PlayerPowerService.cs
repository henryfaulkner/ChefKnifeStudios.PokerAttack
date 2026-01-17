using Ardalis.Result;
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
    Result<IEnumerable<PlayerPowerDTO>> GetPlayerPowers(int count);
    Task<Result<PlayerPowerDTO>> SelectPlayerPowerAsync(string gameId, string playerId, string powerId, CancellationToken ct = default);
    Task<Result> AutoSelectPlayerPowerAsync(string playerId, CancellationToken ct = default);
    Task<Result> ActivateAsync(string gameId, string playerId, CancellationToken ct = default);
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
    public Result<IEnumerable<PlayerPowerDTO>> GetPlayerPowers(int count)
    {
        var result = powerRepository.GetRandomNumber(count);
        if (!result.IsSuccess)
            return Result.Error("Failed to retrieve player powers.");

        return Result.Success(result.Value.Select(x => x.MapToDTO()));
    }

    public async Task<Result<PlayerPowerDTO>> SelectPlayerPowerAsync(string gameId, string playerId, string powerId, CancellationToken ct = default)
    {
        var gamePlayerResult = await gamePlayerRepository.GetAsync(playerId, ct);
        if (!gamePlayerResult.IsSuccess || gamePlayerResult.Value is null)
            return Result.NotFound("Game Player not found");

        var gamePlayer = gamePlayerResult.Value;

        var playerPowerResult = powerRepository.Get(powerId);
        if (!playerPowerResult.IsSuccess || playerPowerResult.Value is null)
            return Result.NotFound("Player Power not found");

        var playerPower = playerPowerResult.Value;
        gamePlayer.PlayerPower = playerPower;

        var updateResult = await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update game player.");

        var activeGameResult = await activeGameRepository.GetAsync(gameId, ct);
        if (activeGameResult.IsSuccess && activeGameResult.Value is ActiveGame activeGame)
        {
            var allHavePowerResult = await DoesEveryPlayersInGameHaveAPower(activeGame, ct);
            if (!allHavePowerResult.IsSuccess)
                return Result.Error("Failed to check player powers.");

            if (allHavePowerResult.Value)
            {
                var transitionResult = await gameStateMachineService.TransitionAsync(gameId, GameEvents.Next, ct);
                if (!transitionResult.IsSuccess)
                    return Result.Error("Failed to transition game state.");
            }
        }

        return Result.Success(playerPower.MapToDTO());
    }

    public async Task<Result> AutoSelectPlayerPowerAsync(string playerId, CancellationToken ct = default)
    {
        var gamePlayerResult = await gamePlayerRepository.GetAsync(playerId, ct);
        if (!gamePlayerResult.IsSuccess || gamePlayerResult.Value is null)
            return Result.NotFound("Game Player not found");

        var gamePlayer = gamePlayerResult.Value;

        // If player already has a power, return success (already selected)
        if (gamePlayer.PlayerPower is not null)
            return Result.Success();

        // Get 1 random power
        var powerResult = GetPlayerPowers(1);
        if (!powerResult.IsSuccess || !powerResult.Value.Any())
            return Result.Error("Failed to retrieve random player power for auto-selection");

        var randomPowerDTO = powerResult.Value.First();
        var playerPowerResult = powerRepository.Get(randomPowerDTO.Id);
        if (!playerPowerResult.IsSuccess || playerPowerResult.Value is null)
            return Result.NotFound("Player Power not found");

        var playerPower = playerPowerResult.Value;

        // Assign power to player
        gamePlayer.PlayerPower = playerPower;

        var updateResult = await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, ct);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update game player with auto-selected power");

        logger.LogInformation("Auto-selected power {PowerName} for player {PlayerId}",
            playerPower.Name, playerId);

        return Result.Success();
    }

    public async Task<Result> ActivateAsync(string gameId, string sourcePlayerId, CancellationToken ct = default)
    {
        var activeGameResult = await activeGameRepository.GetAsync(gameId, ct);
        if (!activeGameResult.IsSuccess || activeGameResult.Value is null)
            return Result.NotFound("Lobby not found");

        var activeGame = activeGameResult.Value;
        var players = activeGame.Players.ToHashSet();

        var sourcePlayer = players.FirstOrDefault(x => x.Id == sourcePlayerId);
        players.RemoveWhere(x => x.Id == sourcePlayerId);
        var targetPlayerIfApplicable = players.Any() ? players.ToList()[Random.Shared.Next(0, players.Count())] : null;
        var targetGamePlayerIdIfApplicable = targetPlayerIfApplicable?.Id;

        var sourceGamePlayerResult = await gamePlayerRepository.GetAsync(sourcePlayerId, ct);
        if (!sourceGamePlayerResult.IsSuccess || sourceGamePlayerResult.Value is null)
            return Result.NotFound("Source Game Player not found");

        var sourceGamePlayer = sourceGamePlayerResult.Value;

        GamePlayer? targetGamePlayer = null;
        if (targetGamePlayerIdIfApplicable is string)
        {
            var targetResult = await gamePlayerRepository.GetAsync(targetGamePlayerIdIfApplicable, ct);
            if (targetResult.IsSuccess && targetResult.Value is not null)
                targetGamePlayer = targetResult.Value;
        }

        var power = sourceGamePlayer.PlayerPower;

        if (power == null)
        {
            logger.LogWarning("Player tried to activate a power but has none equipped.");
            return Result.Success();
        }

        // Prevent manual activation of passive powers
        if (power.PowerKind == PowerKind.Passive)
        {
            logger.LogWarning("Passive power '{PowerName}' cannot be activated manually.", power.Name);
            return Result.Success();
        }

        // Check power points/energy
        if (sourceGamePlayer.PowerPoints < power.PointCost)
        {
            logger.LogDebug("Player does not have enough PP to activate {PowerName}.",
                power.Name);
            return Result.Success();
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
                        return Result.Invalid(new ValidationError($"Power {power.Name} requires a target player."));
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

            var effectResult = effectRegistry.Get(effectInstance.Type);
            if (!effectResult.IsSuccess || effectResult.Value is null)
            {
                logger.LogError("Failed to get effect {EffectType} for power {PowerName}.",
                    effectInstance?.Type, power.Name);
                continue;
            }

            try
            {
                effectResult.Value.Apply(sourceGamePlayer, actualTarget, effectInstance.Parameters);
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

        return Result.Success();
    }

    async Task<Result<bool>> DoesEveryPlayersInGameHaveAPower(ActiveGame activeGame, CancellationToken ct = default)
    {
        bool result = true;
        foreach (var player in activeGame.Players)
        {
            var gamePlayerResult = await gamePlayerRepository.GetAsync(player.Id, ct);
            if (!gamePlayerResult.IsSuccess || gamePlayerResult.Value is null)
                return Result.NotFound("Game Player not found");

            if (gamePlayerResult.Value.PlayerPower is not PlayerPower)
                result = false;
        }
        return Result.Success(result);
    }
}
