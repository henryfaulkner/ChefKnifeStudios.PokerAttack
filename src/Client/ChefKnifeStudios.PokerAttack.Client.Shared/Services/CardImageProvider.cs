using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface ICardImageProvider
{
    string GetCardImagePath(Suits suit, Values value);
}

public class CardImageProvider : ICardImageProvider
{
    static readonly Dictionary<(Suits, Values), string> _cardImagePaths = new()
    {
        // Clubs
        {(Suits.Club, Values.Ace), FilePaths.AceOfClubs},
        {(Suits.Club, Values.Two), FilePaths.TwoOfClubs},
        {(Suits.Club, Values.Three), FilePaths.ThreeOfClubs},
        {(Suits.Club, Values.Four), FilePaths.FourOfClubs},
        {(Suits.Club, Values.Five), FilePaths.FiveOfClubs},
        {(Suits.Club, Values.Six), FilePaths.SixOfClubs},
        {(Suits.Club, Values.Seven), FilePaths.SevenOfClubs},
        {(Suits.Club, Values.Eight), FilePaths.EightOfClubs},
        {(Suits.Club, Values.Nine), FilePaths.NineOfClubs},
        {(Suits.Club, Values.Ten), FilePaths.TenOfClubs},
        {(Suits.Club, Values.Jack), FilePaths.JackOfClubs},
        {(Suits.Club, Values.Queen), FilePaths.QueenOfClubs},
        {(Suits.Club, Values.King), FilePaths.KingOfClubs},

        // Diamonds
        {(Suits.Diamond, Values.Ace), FilePaths.AceOfDiamonds},
        {(Suits.Diamond, Values.Two), FilePaths.TwoOfDiamonds},
        {(Suits.Diamond, Values.Three), FilePaths.ThreeOfDiamonds},
        {(Suits.Diamond, Values.Four), FilePaths.FourOfDiamonds},
        {(Suits.Diamond, Values.Five), FilePaths.FiveOfDiamonds},
        {(Suits.Diamond, Values.Six), FilePaths.SixOfDiamonds},
        {(Suits.Diamond, Values.Seven), FilePaths.SevenOfDiamonds},
        {(Suits.Diamond, Values.Eight), FilePaths.EightOfDiamonds},
        {(Suits.Diamond, Values.Nine), FilePaths.NineOfDiamonds},
        {(Suits.Diamond, Values.Ten), FilePaths.TenOfDiamonds},
        {(Suits.Diamond, Values.Jack), FilePaths.JackOfDiamonds},
        {(Suits.Diamond, Values.Queen), FilePaths.QueenOfDiamonds},
        {(Suits.Diamond, Values.King), FilePaths.KingOfDiamonds},

        // Hearts
        {(Suits.Heart, Values.Ace), FilePaths.AceOfHearts},
        {(Suits.Heart, Values.Two), FilePaths.TwoOfHearts},
        {(Suits.Heart, Values.Three), FilePaths.ThreeOfHearts},
        {(Suits.Heart, Values.Four), FilePaths.FourOfHearts},
        {(Suits.Heart, Values.Five), FilePaths.FiveOfHearts},
        {(Suits.Heart, Values.Six), FilePaths.SixOfHearts},
        {(Suits.Heart, Values.Seven), FilePaths.SevenOfHearts},
        {(Suits.Heart, Values.Eight), FilePaths.EightOfHearts},
        {(Suits.Heart, Values.Nine), FilePaths.NineOfHearts},
        {(Suits.Heart, Values.Ten), FilePaths.TenOfHearts},
        {(Suits.Heart, Values.Jack), FilePaths.JackOfHearts},
        {(Suits.Heart, Values.Queen), FilePaths.QueenOfHearts},
        {(Suits.Heart, Values.King), FilePaths.KingOfHearts},

        // Spades
        {(Suits.Spade, Values.Ace), FilePaths.AceOfSpades},
        {(Suits.Spade, Values.Two), FilePaths.TwoOfSpades},
        {(Suits.Spade, Values.Three), FilePaths.ThreeOfSpades},
        {(Suits.Spade, Values.Four), FilePaths.FourOfSpades},
        {(Suits.Spade, Values.Five), FilePaths.FiveOfSpades},
        {(Suits.Spade, Values.Six), FilePaths.SixOfSpades},
        {(Suits.Spade, Values.Seven), FilePaths.SevenOfSpades},
        {(Suits.Spade, Values.Eight), FilePaths.EightOfSpades},
        {(Suits.Spade, Values.Nine), FilePaths.NineOfSpades},
        {(Suits.Spade, Values.Ten), FilePaths.TenOfSpades},
        {(Suits.Spade, Values.Jack), FilePaths.JackOfSpades},
        {(Suits.Spade, Values.Queen), FilePaths.QueenOfSpades},
        {(Suits.Spade, Values.King), FilePaths.KingOfSpades},
    };

    public string GetCardImagePath(Suits suit, Values value) =>
        _cardImagePaths.TryGetValue((suit, value), out var path) ? path : string.Empty;
}
