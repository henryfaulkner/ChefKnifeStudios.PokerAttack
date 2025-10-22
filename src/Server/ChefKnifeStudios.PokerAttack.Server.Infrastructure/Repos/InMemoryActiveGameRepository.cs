using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure.Repos;

public class InMemoryActiveGameRepository : IActiveGameRepository
{
    readonly Dictionary<string, ActiveGame> _activeGames = new();

    public Task AddAsync(string gameId, ActiveGame gameState, CancellationToken cancellationToken = default)
    {
        if (_activeGames.ContainsKey(gameId))
            throw new InvalidOperationException($"Player {gameId} already exists.");

        _activeGames[gameId] = gameState;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(string gameId, ActiveGame gameState, CancellationToken cancellationToken = default)
    {
        if (!_activeGames.ContainsKey(gameId))
            throw new KeyNotFoundException($"Player {gameId} not found.");

        _activeGames[gameId] = gameState;
        return Task.CompletedTask;
    }

    public Task<ActiveGame?> GetAsync(string gameId, CancellationToken cancellationToken = default)
    {
        ActiveGame? gameState = _activeGames.ContainsKey(gameId) ? _activeGames[gameId] : null;
        return Task.FromResult(gameState);
    }

    public Task<IReadOnlyDictionary<string, ActiveGame>> GetAllAsync(CancellationToken ct = default)
    {
        return Task.FromResult((IReadOnlyDictionary<string, ActiveGame>)_activeGames);
    }

    public Task DeleteAsync(string gameId, CancellationToken cancellationToken = default)
    {
        _activeGames.Remove(gameId);
        return Task.CompletedTask;
    }
}
