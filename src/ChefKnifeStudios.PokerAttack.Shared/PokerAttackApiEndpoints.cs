namespace ChefKnifeStudios.PokerAttack.Shared;

public class PokerAttackApiEndpoints
{
    public class Test
    {
        public const string SignalR = "/test/signal-r";
        public const string Cleanup = "/test/cleanup";
    }

    public class Lobby
    {
        const string EndpointGroup = "/lobby";

        public const string GetLobbies = $"{EndpointGroup}";
        public const string GetLobby = $"{EndpointGroup}/{{gameId}}";
        public const string CreateLobby = $"{EndpointGroup}/create-lobby";
        public const string AddPlayer = $"{EndpointGroup}/add-player";
        public const string RemovePlayer = $"{EndpointGroup}/remove-player";
        public const string UpdatePlayer = $"{EndpointGroup}/update-player";
        public const string ShutdownLobby = $"{EndpointGroup}/shutdown-lobby/{{gameId}}";
        public const string StartGame = $"{EndpointGroup}/start-game";
    }

    public class Gameplay
    {
        const string EndpointGroup = "/gameplay";

        public const string GetLatestRound = $"{EndpointGroup}/latest-round/{{gameId}}";
        public const string GetPlayerWallet = $"{EndpointGroup}/player-wallet/{{gameId}}/{{playerId}}";
        public const string LeaveGame = $"{EndpointGroup}/leave-game/{{gameId}}/{{playerId}}";
    }

    public class PlayerPower
    {
        const string EndpointGroup = "/player-power";

        public const string GetPlayerPowers = $"{EndpointGroup}/{{countStr}}";
        public const string SelectPlayerPower = $"{EndpointGroup}/{{gameId}}/{{playerId}}/{{powerId}}";
    }

    public class Shop
    {
        const string EndpointGroup = "/shop";

        public const string GetShopItems = $"{EndpointGroup}/shop-items/{{gameId}}";
        public const string PurchaseShopItem = $"{EndpointGroup}/{{gameId}}/{{playerId}}/{{shopItemId}}";
    }

    public class ScoringRules
    {
        const string EndpointGroup = "/scoring-rules";

        public const string GetScoringRules = $"{EndpointGroup}";
    }

    public class Upload
    {
        const string EndpointGroup = "/upload";

        public const string PublishRecording = $"{EndpointGroup}/recording";
    }

    public class SoloGameplay
    {
        const string EndpointGroup = "/solo";

        public const string StartGame = $"{EndpointGroup}/start-game";
        public const string PlayHand = $"{EndpointGroup}/play-hand/{{gameId}}";
        public const string Discard = $"{EndpointGroup}/discard/{{gameId}}";
        public const string GetGameState = $"{EndpointGroup}/game-state/{{gameId}}";
        public const string PurchaseShopItem = $"{EndpointGroup}/purchase/{{gameId}}/{{shopItemId}}";
        public const string AdvancePhase = $"{EndpointGroup}/advance-phase/{{gameId}}";
    }
}
