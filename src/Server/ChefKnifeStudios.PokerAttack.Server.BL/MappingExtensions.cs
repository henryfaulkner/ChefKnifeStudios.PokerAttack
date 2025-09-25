using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

namespace ChefKnifeStudios.PokerAttack.Server.BL;

public static class MappingExtensions
{
    public static LobbyDTO MapToDTO(this Lobby model, string gameId)
    {
        return new()
        {
            GameId = gameId,
            HostPlayer = model.HostPlayer?.MapToDTO(),
            Players = model.Players.Select(x => x.MapToDTO()).ToHashSet(),
        };
    }

    public static Lobby MapToModel(this LobbyDTO dto)
    {
        return new()
        {
            HostPlayer = dto.HostPlayer?.MapToModel(),
            Players = dto.Players.Select(x => x.MapToModel()).ToHashSet(),
        };
    }

    public static PlayerDTO MapToDTO(this Player model)
    {
        return new()
        {
            Id = model.Id,
            Name = model.Name,
        };
    }

    public static Player MapToModel(this PlayerDTO dto)
    {
        return new()
        {
            Id = dto.Id,
            Name = dto.Name,
        };
    }
}
