using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IShopService
{
    Task<ItemBase> PurchaseItemAsync(string gameId, string playerId, string itemId, CancellationToken cancellationToken = default);
}

public class ShopService : IShopService
{
    readonly IItemRepository _itemRepository;
    readonly IKeyValueRepository<GamePlayer> _gamePlayerRepository;

    public ShopService(
        IItemRepository itemRepository,
        IKeyValueRepository<GamePlayer> gamePlayerRepository)
    {
        _itemRepository = itemRepository;
        _gamePlayerRepository = gamePlayerRepository;
    }

    public async Task<ItemBase> PurchaseItemAsync(string gameId, string playerId, string itemId, CancellationToken cancellationToken = default)
    {
        // Get the item from the repository
        var item = _itemRepository.Get(itemId)
            ?? throw new KeyNotFoundException($"Item not found with ID: {itemId}");

        // Get the game player
        var gamePlayer = await _gamePlayerRepository.GetAsync(playerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Game player not found with ID: {playerId}");

        // Check if player has enough wallet balance
        if (gamePlayer.Wallet < item.Price)
        {
            throw new InvalidOperationException($"Insufficient funds. Player wallet: {gamePlayer.Wallet}, Item price: {item.Price}");
        }

        // Deduct the price from wallet
        gamePlayer.Wallet -= item.Price;

        // Add the item to purchased items
        gamePlayer.PurchasedItems.Add(item);

        // Update the game player
        await _gamePlayerRepository.UpdateAsync(playerId, gamePlayer, cancellationToken);

        return item;
    }
}
