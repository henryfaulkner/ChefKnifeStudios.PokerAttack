namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.SoloGameplay;

public record StartSoloGameReqDTO(string PlayerName);

public record StartSoloGameResDTO(
    string GameId,
    string PlayerId,
    IEnumerable<Gameplay.CardDTO> Cards,
    int HandsAvailable,
    int DiscardsAvailable,
    int Wallet,
    int Score,
    int RoundNumber
);

public record PlayHandReqDTO(string PlayerId, List<Gameplay.CardDTO> Hand);

public record PlayHandResDTO(
    Gameplay.HandResultDTO HandResult,
    IEnumerable<Gameplay.CardDTO> NewCards,
    int HandsRemaining,
    int Wallet,
    int Score
);

public record DiscardReqDTO(string PlayerId, List<Gameplay.CardDTO> DiscardCards);

public record DiscardResDTO(
    IEnumerable<Gameplay.CardDTO> NewCards,
    int DiscardsRemaining
);

public record SoloGameStateDTO(
    string GameId,
    string PlayerId,
    IEnumerable<Gameplay.CardDTO> Cards,
    int HandsAvailable,
    int DiscardsAvailable,
    int Wallet,
    int Score,
    int RoundNumber,
    string Phase
);

public record PurchaseItemResDTO(
    ShopItemDTO PurchasedItem,
    int RemainingWallet
);

public record AdvancePhaseResDTO(
    string NewPhase,
    IEnumerable<Gameplay.CardDTO>? NewCards,
    int? HandsAvailable,
    int? DiscardsAvailable,
    IEnumerable<ShopItemDTO>? ShopItems
);
