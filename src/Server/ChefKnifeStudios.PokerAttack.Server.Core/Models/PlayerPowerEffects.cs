using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public interface IPlayerPowerEffect
{
    void Apply(GamePlayer source, GamePlayer target, Dictionary<string, string>? args);
}

public class UnflipAllCardsEffect : IPlayerPowerEffect
{
    public void Apply(GamePlayer source, GamePlayer target, Dictionary<string, string>? args)
    {
        target.UnflipAllCards();
    }
}

public class FlipRandomCardsEffect : IPlayerPowerEffect
{
    public void Apply(GamePlayer source, GamePlayer target, Dictionary<string, string>? args)
    {
        int count = int.Parse(args?["count"] ?? "1");
        target.FlipRandomCards(count);
    }
}

public class ChangeRandomCardsToRandomSuitEffect : IPlayerPowerEffect
{
    public void Apply(GamePlayer source, GamePlayer target, Dictionary<string, string>? args)
    {
        int count = int.Parse(args?["count"] ?? "1");
        target.ChangeRandomCardsToRandomSuit(count);
    }
}

public class ChangeSelectedCardsToSuitEffect : IPlayerPowerEffect
{
    public void Apply(GamePlayer source, GamePlayer target, Dictionary<string, string>? args)
    {
        Suits suit = Enum.Parse<Suits>(args?["suit"] ?? "0");
        var selectedCards = args?["selectedCards"] is string
            ? (JsonSerializer.Deserialize<IEnumerable<Card>>(args["selectedCards"], JsonOptions.Get()) ?? [])
            : [];
        target.ChangeSelectedCardsToSuit(suit, selectedCards);
    }
}
