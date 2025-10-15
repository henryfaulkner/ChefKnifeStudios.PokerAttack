using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure.Repos;

public class InMemoryGameStateRepository : IGameStateRepository 
{
    readonly Dictionary<string, GameStates> _gameStates = new();

    public Task AddAsync(string gameId, GameStates gameState, CancellationToken cancellationToken = default)
    {
        if (_gameStates.ContainsKey(gameId))
            throw new InvalidOperationException($"Player {gameId} already exists.");

        _gameStates[gameId] = gameState;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(string gameId, GameStates gameState, CancellationToken cancellationToken = default)
    {
        if (!_gameStates.ContainsKey(gameId))
            throw new KeyNotFoundException($"Player {gameId} not found.");

        _gameStates[gameId] = gameState;
        return Task.CompletedTask;
    }

    public Task<GameStates?> GetAsync(string gameId, CancellationToken cancellationToken = default)
    {
        GameStates? gameState = _gameStates.ContainsKey(gameId) ? _gameStates[gameId] : null;
        return Task.FromResult(gameState);
    }

    public Task DeleteAsync(string gameId, CancellationToken cancellationToken = default)
    {
        _gameStates.Remove(gameId);
        return Task.CompletedTask;
    }
}
