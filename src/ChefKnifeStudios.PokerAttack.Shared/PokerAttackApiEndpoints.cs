namespace ChefKnifeStudios.PokerAttack.Shared;

public class PokerAttackApiEndpoints
{
    public class Test
    {
        public const string SignalR = "/test/signal-r";
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
        public const string StartGame = $"{EndpointGroup}/start-game/{{gameId}}";
    }

    public class Gameplay
    {
        const string EndpointGroup = "/gameplay";

        public const string GetLatestRound = $"{EndpointGroup}/latest-round/{{gameId}}";
    }

    public class PlayerPower
    {
        const string EndpointGroup = "/player-power";

        public const string GetSomePowers = $"{EndpointGroup}/{{countStr}}";
        public const string SelectPlayerPower = $"{EndpointGroup}/{{gameId}}/{{playerId}}/{{powerId}}";
    }
}
