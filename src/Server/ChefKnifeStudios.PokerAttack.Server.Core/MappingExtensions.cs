using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI;

public static class MappingExtensions
{
    public static LobbyDTO MapToDTO(this Lobby lobby, string gameId)
    {
        return new()
        {
            GameId = gameId,
            HostPlayerId = lobby.HostPlayerId,
            PlayerIds = lobby.PlayerIds,
        };
    }

    public static Lobby MapToModel(this LobbyDTO dto)
    {
        return new()
        {
            HostPlayerId = dto.HostPlayerId,
            PlayerIds = dto.PlayerIds,
        };
    }
}
