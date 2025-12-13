using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

public record StartGameDTO(string LobbyId, GameModes GameMode);
