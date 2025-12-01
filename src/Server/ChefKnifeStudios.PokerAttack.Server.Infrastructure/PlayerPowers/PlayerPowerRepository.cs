using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure.PlayerPowers;

public class PlayerPowerRepository : IPlayerPowerRepository
{
    readonly Dictionary<string, PlayerPower> _powers = new();
    static readonly Random _rng = new Random();

    public PlayerPowerRepository()
    {
        var powers = JsonSerializer.Deserialize<List<PlayerPower>>(_json, JsonOptions.Get()) ?? new List<PlayerPower>();

        foreach (var power in powers)
        {
            _powers[power.Id] = power;
        }
    }

    // Get a single power by ID
    public PlayerPower? Get(string id)
    {
        _powers.TryGetValue(id, out var power);
        return power;
    }

    // Get all powers
    public IEnumerable<PlayerPower> GetAll() => _powers.Values;

    public IEnumerable<PlayerPower> GetRandomNumber(int count = 3)
    {
        List<PlayerPower> allPowers = _powers.Select(x => x.Value).ToList();

        if (!allPowers.Any()) return Array.Empty<PlayerPower>();

        // Randomly pick powers without replacement
        var selected = new List<PlayerPower>();
        var available = new List<PlayerPower>(allPowers);

        for (int i = 0; i < count && available.Any(); i++)
        {
            int index = _rng.Next(available.Count);
            selected.Add(available[index]);
            available.RemoveAt(index);
        }

        return selected;
    }

    const string _json =
        """
        [
          {
            "id": "unflip_all",
            "name": "Unflip All",
            "description": "Turns all your cards face-up.",
            "powerKind": "active",
            "pointCost": 0,
            "effects": [
              {
                "type": "unflipAllCards",
                "powerMessage": "All cards were flipped face-up",
                "target": "self",
                "parameters": {},
                "trigger": "none"
              }
            ]
          },
          {
            "id": "flip_random",
            "name": "Flip Random",
            "description": "Flips 2 of an opponant's cards at random.",
            "powerKind": "active",
            "pointCost": 0,
            "effects": [
              {
                "type": "flipRandomCards",
                "powerMessage": "The cards were flipped face-down",
                "target": "opponent",
                "parameters": { "count": "2" },
                "trigger": "none"
              }
            ]
          },
          {
            "id": "change_random_suit",
            "name": "Change Suit Random",
            "description": "Changes the suit of 2 of an opponant's cards at random.",
            "powerKind": "active",
            "pointCost": 0,
            "effects": [
              {
                "type": "changeRandomCardsToRandomSuit",
                "powerMessage": "The suit of 2 cards were changed",
                "target": "opponent",
                "parameters": { "count": "2" },
                "trigger": "none"
              }
            ]
          },
          {
            "id": "change_random_rank",
            "name": "Change Rank Random",
            "description": "Changes the rank of 2 of an opponant's cards at random.",
            "powerKind": "active",
            "pointCost": 0,
            "effects": [
              {
                "type": "changeRandomCardsToRandomRank",
                "powerMessage": "The rank of 2 cards were changed",
                "target": "opponent",
                "parameters": { "count": "2" },
                "trigger": "none"
              }
            ]
          }
        ]
        """;
}
