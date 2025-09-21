using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure.Repos;

public class InMemoryPlayerScoreRepository : IPlayerScoreRepository
{
    readonly Dictionary<string, int> _playerScores = new();

    public Task AddAsync(string playerId, int score, CancellationToken cancellationToken = default)
    {
        if (_playerScores.ContainsKey(playerId))
            throw new InvalidOperationException($"Player {playerId} already exists.");

        _playerScores[playerId] = score;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(string playerId, int score, CancellationToken cancellationToken = default)
    {
        if (!_playerScores.ContainsKey(playerId))
            throw new KeyNotFoundException($"Player {playerId} not found.");

        _playerScores[playerId] = score;
        return Task.CompletedTask;
    }

    public Task<int?> GetAsync(string playerId, CancellationToken cancellationToken = default)
    {
        int? score = _playerScores.ContainsKey(playerId) ? _playerScores[playerId] : null;
        return Task.FromResult(score);
    }

    public Task DeleteAsync(string playerId, CancellationToken cancellationToken = default)
    {
        _playerScores.Remove(playerId);
        return Task.CompletedTask;
    }

    public Task<Dictionary<string, int>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Dictionary<string, int>(_playerScores));
    }
}
