namespace ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;

public class MultiGameResultModalEventArgs : ModalEventArgs
{
    public required string GameResult { get; init; }
}
