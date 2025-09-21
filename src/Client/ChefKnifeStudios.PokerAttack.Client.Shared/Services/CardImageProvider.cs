using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface ICardImageProvider
{
    string GetCardImagePath(Suits suit, Ranks value);
}

public class CardImageProvider : ICardImageProvider
{
    static readonly Dictionary<(Suits, Ranks), string> _cardImagePaths = new()
    {
        // Clubs
        {(Suits.Club, Ranks.Ace), FilePaths.AceOfClubs},
        {(Suits.Club, Ranks.Two), FilePaths.TwoOfClubs},
        {(Suits.Club, Ranks.Three), FilePaths.ThreeOfClubs},
        {(Suits.Club, Ranks.Four), FilePaths.FourOfClubs},
        {(Suits.Club, Ranks.Five), FilePaths.FiveOfClubs},
        {(Suits.Club, Ranks.Six), FilePaths.SixOfClubs},
        {(Suits.Club, Ranks.Seven), FilePaths.SevenOfClubs},
        {(Suits.Club, Ranks.Eight), FilePaths.EightOfClubs},
        {(Suits.Club, Ranks.Nine), FilePaths.NineOfClubs},
        {(Suits.Club, Ranks.Ten), FilePaths.TenOfClubs},
        {(Suits.Club, Ranks.Jack), FilePaths.JackOfClubs},
        {(Suits.Club, Ranks.Queen), FilePaths.QueenOfClubs},
        {(Suits.Club, Ranks.King), FilePaths.KingOfClubs},

        // Diamonds
        {(Suits.Diamond, Ranks.Ace), FilePaths.AceOfDiamonds},
        {(Suits.Diamond, Ranks.Two), FilePaths.TwoOfDiamonds},
        {(Suits.Diamond, Ranks.Three), FilePaths.ThreeOfDiamonds},
        {(Suits.Diamond, Ranks.Four), FilePaths.FourOfDiamonds},
        {(Suits.Diamond, Ranks.Five), FilePaths.FiveOfDiamonds},
        {(Suits.Diamond, Ranks.Six), FilePaths.SixOfDiamonds},
        {(Suits.Diamond, Ranks.Seven), FilePaths.SevenOfDiamonds},
        {(Suits.Diamond, Ranks.Eight), FilePaths.EightOfDiamonds},
        {(Suits.Diamond, Ranks.Nine), FilePaths.NineOfDiamonds},
        {(Suits.Diamond, Ranks.Ten), FilePaths.TenOfDiamonds},
        {(Suits.Diamond, Ranks.Jack), FilePaths.JackOfDiamonds},
        {(Suits.Diamond, Ranks.Queen), FilePaths.QueenOfDiamonds},
        {(Suits.Diamond, Ranks.King), FilePaths.KingOfDiamonds},

        // Hearts
        {(Suits.Heart, Ranks.Ace), FilePaths.AceOfHearts},
        {(Suits.Heart, Ranks.Two), FilePaths.TwoOfHearts},
        {(Suits.Heart, Ranks.Three), FilePaths.ThreeOfHearts},
        {(Suits.Heart, Ranks.Four), FilePaths.FourOfHearts},
        {(Suits.Heart, Ranks.Five), FilePaths.FiveOfHearts},
        {(Suits.Heart, Ranks.Six), FilePaths.SixOfHearts},
        {(Suits.Heart, Ranks.Seven), FilePaths.SevenOfHearts},
        {(Suits.Heart, Ranks.Eight), FilePaths.EightOfHearts},
        {(Suits.Heart, Ranks.Nine), FilePaths.NineOfHearts},
        {(Suits.Heart, Ranks.Ten), FilePaths.TenOfHearts},
        {(Suits.Heart, Ranks.Jack), FilePaths.JackOfHearts},
        {(Suits.Heart, Ranks.Queen), FilePaths.QueenOfHearts},
        {(Suits.Heart, Ranks.King), FilePaths.KingOfHearts},

        // Spades
        {(Suits.Spade, Ranks.Ace), FilePaths.AceOfSpades},
        {(Suits.Spade, Ranks.Two), FilePaths.TwoOfSpades},
        {(Suits.Spade, Ranks.Three), FilePaths.ThreeOfSpades},
        {(Suits.Spade, Ranks.Four), FilePaths.FourOfSpades},
        {(Suits.Spade, Ranks.Five), FilePaths.FiveOfSpades},
        {(Suits.Spade, Ranks.Six), FilePaths.SixOfSpades},
        {(Suits.Spade, Ranks.Seven), FilePaths.SevenOfSpades},
        {(Suits.Spade, Ranks.Eight), FilePaths.EightOfSpades},
        {(Suits.Spade, Ranks.Nine), FilePaths.NineOfSpades},
        {(Suits.Spade, Ranks.Ten), FilePaths.TenOfSpades},
        {(Suits.Spade, Ranks.Jack), FilePaths.JackOfSpades},
        {(Suits.Spade, Ranks.Queen), FilePaths.QueenOfSpades},
        {(Suits.Spade, Ranks.King), FilePaths.KingOfSpades},
    };

    public string GetCardImagePath(Suits suit, Ranks value) =>
        _cardImagePaths.TryGetValue((suit, value), out var path) ? path : string.Empty;
}
