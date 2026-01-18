namespace ChefKnifeStudios.PokerAttack.Shared.DTOs;

public record GameSettingsDTO(
    int RoundTimeMs,
    int ShopTimeMs,
    int EliminationTimeMs,
    int PlayerPowerSelectionTimeMs
);
