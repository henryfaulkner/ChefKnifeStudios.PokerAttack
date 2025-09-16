using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface ILobbyService
{
    Task CreateLobbyAsync(string gameId);
    Task JoinLobbyAsync(string gameId, string playerId);
    Task LeaveLobbyAsync(string gameId, string playerId);
    Task<IEnumerable<string>> GetPlayersAsync(string gameId);
}

public class LobbyService : ILobbyService
{
    private readonly ILobbyRepository _lobbyRepository;

    public LobbyService(
        ILobbyRepository lobbyRepository)
    {
        _lobbyRepository = lobbyRepository;
    }

    /// <summary>
    /// Creates a new lobby (empty) with the given gameId.
    /// </summary>
    public async Task CreateLobbyAsync(string gameId)
    {
        // Optionally check if lobby already exists
        bool exists = await _lobbyRepository.LobbyExistsAsync(gameId);
        if (exists)
            throw new InvalidOperationException("Lobby already exists.");

        // Create empty lobby in Redis or cache
        await _lobbyRepository.CreateLobbyAsync(gameId);
    }

    /// <summary>
    /// Adds a player to an existing lobby.
    /// </summary>
    public async Task JoinLobbyAsync(string gameId, string playerId)
    {
        // Business rule: player not already in lobby
        if (await _lobbyRepository.IsPlayerInLobbyAsync(gameId, playerId))
            throw new InvalidOperationException("Player already in the lobby.");

        await _lobbyRepository.AddPlayerToLobbyAsync(gameId, playerId);
    }

    /// <summary>
    /// Removes a player from a lobby.
    /// </summary>
    public async Task LeaveLobbyAsync(string gameId, string playerId)
    {
        await _lobbyRepository.RemovePlayerFromLobbyAsync(gameId, playerId);
    }

    /// <summary>
    /// Gets all players in a lobby.
    /// </summary>
    public async Task<IEnumerable<string>> GetPlayersAsync(string gameId)
    {
        return await _lobbyRepository.GetPlayersAsync(gameId);
    }
}

