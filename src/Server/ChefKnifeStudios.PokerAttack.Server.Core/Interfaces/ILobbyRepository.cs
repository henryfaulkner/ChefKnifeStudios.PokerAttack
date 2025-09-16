using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

public interface ILobbyRepository
{
    Task<LobbyDTO> CreateLobbyAsync(string gameId, CancellationToken cancellationToken = default);
    Task<bool> LobbyExistsAsync(string gameId, CancellationToken cancellationToken = default);
    Task AddPlayerToLobbyAsync(string gameId, string playerId, CancellationToken cancellationToken = default);
    Task RemovePlayerFromLobbyAsync(string gameId, string playerId, CancellationToken cancellationToken = default);
    Task<bool> IsPlayerInLobbyAsync(string gameId, string playerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> GetPlayersAsync(string gameId, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> ShutDownLobbyAsync(string gameId, CancellationToken cancellationToken = default);
    Task<IEnumerable<LobbyDTO>> GetLobbiesAsync(CancellationToken cancellationToken = default);
    Task<LobbyDTO?> GetLobbyAsync(string gameId, CancellationToken cancellationToken = default);
}
