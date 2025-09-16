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
        public const string ShutdownLobby = $"{EndpointGroup}/shutdown-lobby/{{gameId}}";
    }
}
