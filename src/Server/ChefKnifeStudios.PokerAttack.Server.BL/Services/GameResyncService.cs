using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IGameResyncService
{
    Task<Result<GameStateDTO>> GetGameStateAsync(string gameId, string playerId, CancellationToken ct = default);
}

public class GameResyncService(
    IKeyValueRepository<ActiveGame> activeGameRepository,
    IKeyValueRepository<GamePlayer> gamePlayerRepository,
    IClock clock) : IGameResyncService
{
    public async Task<Result<GameStateDTO>> GetGameStateAsync(string gameId, string playerId, CancellationToken ct = default)
    {
        var activeGameResult = await activeGameRepository.GetAsync(gameId, ct);
        if (!activeGameResult.IsSuccess || activeGameResult.Value is not ActiveGame activeGame)
            return Result.NotFound($"Game not found: {gameId}");

        var requestingPlayer = activeGame.Players.FirstOrDefault(p => p.Id == playerId);
        if (requestingPlayer is null)
            return Result.NotFound($"Player {playerId} is not a participant in game {gameId}");

        var gamePlayerResult = await gamePlayerRepository.GetAsync(playerId, ct);
        if (!gamePlayerResult.IsSuccess || gamePlayerResult.Value is not GamePlayer gamePlayer)
            return Result.NotFound($"Player state not found: {playerId}");

        var opponents = new List<ResyncOpponentDTO>();
        foreach (var player in activeGame.Players.Where(p => p.Id != playerId).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var opponentStateResult = await gamePlayerRepository.GetAsync(player.Id, ct);
            var opponentState = opponentStateResult.IsSuccess ? opponentStateResult.Value : null;

            opponents.Add(new ResyncOpponentDTO
            {
                PlayerId = player.Id,
                Name = player.Name,
                Score = opponentState?.Score ?? 0,
                IsEliminating = opponentState?.IsEliminating ?? false,
                IsEliminated = opponentState?.IsEliminated ?? false,
            });
        }

        var playerDTO = new ResyncPlayerDTO
        {
            PlayerId = playerId,
            CardsInHand = gamePlayer.CardsInHand.Select(c => c.MapToDTO()),
            Score = gamePlayer.Score,
            Wallet = gamePlayer.Wallet,
            HandsRemaining = gamePlayer.HandsRemaining,
            DiscardsRemaining = gamePlayer.DiscardsRemaining,
            IsEliminating = gamePlayer.IsEliminating,
            IsEliminated = gamePlayer.IsEliminated,
            PlayerPower = gamePlayer.PlayerPower?.MapToDTO(),
            PurchasedItems = gamePlayer.PurchasedItems,
        };

        return Result.Success(new GameStateDTO
        {
            GameId = gameId,
            RoundNumber = activeGame.RoundNumber,
            CurrentPhase = activeGame.CurrentPhase,
            PhaseStartedAt = activeGame.PhaseStartedAt,
            PhaseExpiresAt = activeGame.PhaseExpiresAt,
            ServerTimeUtc = clock.UtcNow,
            Player = playerDTO,
            Opponents = opponents,
        });
    }
}
