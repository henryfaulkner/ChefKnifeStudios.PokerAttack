namespace ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs.ModalEvents;

public class MultiGameResultModalEventArgs : ModalEventArgs
{
    public required string GameResult { get; init; }
}
