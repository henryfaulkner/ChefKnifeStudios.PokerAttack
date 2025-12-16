using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IShopService
{
    Task<ShopItemDTO> PurchaseItemAsync(string gameId, string playerId, string itemId, CancellationToken cancellationToken = default);
}

public class ShopService : IShopService
{
    readonly IItemRepository _itemRepository;
    readonly IKeyValueRepository<GamePlayer> _gamePlayerRepository;
    readonly IKeyValueRepository<ActiveGame> _activeGameRepository;

    public ShopService(
        IItemRepository itemRepository,
        IKeyValueRepository<GamePlayer> gamePlayerRepository,
        IKeyValueRepository<ActiveGame> activeGameRepository)
    {
        _itemRepository = itemRepository;
        _gamePlayerRepository = gamePlayerRepository;
        _activeGameRepository = activeGameRepository;
    }

    public async Task<ShopItemDTO> PurchaseItemAsync(string gameId, string playerId, string itemId, CancellationToken cancellationToken = default)
    {
        // Get the item from the repository
        var item = _itemRepository.Get(itemId)
            ?? throw new KeyNotFoundException($"Item not found with ID: {itemId}");

        // Get the game player
        var gamePlayer = await _gamePlayerRepository.GetAsync(playerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Game player not found with ID: {playerId}");

        // Get the active game to calculate round-based pricing
        var activeGame = await _activeGameRepository.GetAsync(gameId, cancellationToken)
            ?? throw new KeyNotFoundException($"Active game not found with ID: {gameId}");

        // Calculate adjusted price: 15% increase per round
        var adjustedPrice = (int)(item.Price * (1 + 0.15 * activeGame.RoundNumber));

        // Check if player has enough wallet balance
        if (gamePlayer.Wallet < adjustedPrice)
        {
            throw new InvalidOperationException($"Insufficient funds. Player wallet: {gamePlayer.Wallet}, Item price: {adjustedPrice}");
        }

        // Deduct the price from wallet
        gamePlayer.Wallet -= adjustedPrice;

        // Add the item to purchased items (wagers will be accessible via ActiveWagers property)
        gamePlayer.PurchasedItems.Add(item);

        // Update the game player
        await _gamePlayerRepository.UpdateAsync(playerId, gamePlayer, cancellationToken);

        return new ShopItemDTO
        {
            Item = item,
            AdjustedPrice = adjustedPrice
        };
    }
}
