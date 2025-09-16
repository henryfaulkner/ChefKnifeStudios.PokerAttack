using ChefKnifeStudios.PokerAttack.Server.Core.Models;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

public interface ILobbyRepository
{
    Task AddLobbyAsync(string gameId, Lobby lobby, CancellationToken cancellationToken = default);
    Task<bool> LobbyExistsAsync(string gameId, CancellationToken cancellationToken = default);
    Task<Lobby?> GetLobbyAsync(string gameId, CancellationToken cancellationToken = default);
    Task<IEnumerable<KeyValuePair<string, Lobby>>> GetAllLobbiesAsync(CancellationToken cancellationToken = default);
    Task RemoveLobbyAsync(string gameId, CancellationToken cancellationToken = default);
    Task UpdateLobbyAsync(string gameId, Lobby lobby, CancellationToken cancellationToken = default);
}
