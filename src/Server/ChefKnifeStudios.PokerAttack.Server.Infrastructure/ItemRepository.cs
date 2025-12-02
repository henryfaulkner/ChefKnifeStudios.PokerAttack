using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using ChefKnifeStudios.PokerAttack.Shared;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure;

public class ItemRepository : IItemRepository
{
    readonly Dictionary<string, ItemBase> _items = new();
    readonly Dictionary<RarityTiers, ItemRarity> _rarities = new();
    static readonly Random _rng = new Random();

    public ItemRepository()
    {
        var items = JsonSerializer.Deserialize<List<ItemBase>>(ItemJsonConstants.ItemSchema, JsonOptions.Get()) ?? new List<ItemBase>();
        var rarities = JsonSerializer.Deserialize<List<ItemRarity>>(ItemJsonConstants.RaritySchema, JsonOptions.Get()) ?? new List<ItemRarity>();

        foreach (var rarity in rarities)
        {
            _rarities[rarity.RarityTier] = rarity;
        }

        foreach (var item in items)
        {
            _items[item.Id] = item;
            if (_rarities.TryGetValue(item.RarityTier, out var rarity))
            {
                item.Rarity = rarity;
            }
        }
    }

    public ItemBase? Get(string id)
    {
        _items.TryGetValue(id, out var item);
        return item;
    }

    public IEnumerable<ItemBase> GetAll() => _items.Values;

    public IEnumerable<ItemBase> GetRandomNumber(int count = 3)
    {
        List<ItemBase> allItems = _items.Select(x => x.Value).ToList();
        if (!allItems.Any()) return Array.Empty<ItemBase>();

        // If count is greater than or equal to available items, shuffle and return all
        if (count >= allItems.Count)
        {
            return allItems.OrderBy(x => _rng.Next()).ToList();
        }

        var selected = new List<ItemBase>();

        // Get unique random items
        while (selected.Count < count)
        {
            var item = SelectItemByRarity(allItems);

            // Ensure uniqueness by ID
            if (!selected.Any(x => x.Id == item.Id))
            {
                selected.Add(item);
            }
        }

        return selected;
    }

    ItemBase SelectItemByRarity(List<ItemBase> availableItems)
    {
        // Step 1: Calculate total weight from all available items
        int totalWeight = availableItems.Sum(item => item.Rarity?.Chance ?? 1);

        // Step 2: Generate random value within total weight range
        int randomValue = _rng.Next(totalWeight);

        // Step 3: Select item using cumulative probability
        int cumulativeWeight = 0;
        foreach (var item in availableItems)
        {
            int weight = item.Rarity?.Chance ?? 1;
            cumulativeWeight += weight;

            if (randomValue < cumulativeWeight)
            {
                return item;
            }
        }

        // Fallback (should not reach here)
        return availableItems[0];
    }
}
