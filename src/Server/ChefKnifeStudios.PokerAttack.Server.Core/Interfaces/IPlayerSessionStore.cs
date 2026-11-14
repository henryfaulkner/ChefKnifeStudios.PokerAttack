using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

public interface IPlayerSessionStore
{
    Task<Result> UpsertAsync(PlayerSession session, CancellationToken ct = default);
    Task<Result<PlayerSession?>> GetByPlayerAsync(string playerId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PlayerSession>>> FindByGameAsync(string gameId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PlayerSession>>> FindByLobbyAsync(string lobbyId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PlayerSession>>> FindOverdueByDisconnectedAtAsync(DateTime threshold, CancellationToken ct = default);
}
