using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;

public interface IGameStateRepository
{
    Task AddAsync(string gameId, GameStates gameState, CancellationToken cancellationToken = default);
    Task UpdateAsync(string gameId, GameStates gameState, CancellationToken cancellationToken = default);
    Task<GameStates?> GetAsync(string gameId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string gameId, CancellationToken cancellationToken = default);
}
