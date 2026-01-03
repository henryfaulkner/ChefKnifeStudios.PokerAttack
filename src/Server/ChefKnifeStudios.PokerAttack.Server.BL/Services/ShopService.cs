using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IShopService
{
    Task<Result<ShopItemDTO>> PurchaseItemAsync(string gameId, string playerId, string itemId, CancellationToken cancellationToken = default);
}

public class ShopService(
    IItemRepository itemRepository,
    IKeyValueRepository<GamePlayer> gamePlayerRepository,
    IKeyValueRepository<ActiveGame> activeGameRepository,
    ILogger<ShopService> logger) : IShopService
{

    public async Task<Result<ShopItemDTO>> PurchaseItemAsync(string gameId, string playerId, string itemId, CancellationToken cancellationToken = default)
    {
        // Get the item from the repository
        var itemResult = itemRepository.Get(itemId);
        if (!itemResult.IsSuccess || itemResult.Value is null)
        {
            logger.LogWarning("Purchase failed: ItemId={ItemId}, Reason=ItemNotFound", itemId);
            return Result.NotFound($"Item not found with ID: {itemId}");
        }

        var item = itemResult.Value;

        // Get the game player
        var gamePlayerResult = await gamePlayerRepository.GetAsync(playerId, cancellationToken);
        if (!gamePlayerResult.IsSuccess || gamePlayerResult.Value is null)
        {
            logger.LogWarning("Purchase failed: PlayerId={PlayerId}, ItemId={ItemId}, Reason=PlayerNotFound",
                playerId, itemId);
            return Result.NotFound($"Game player not found with ID: {playerId}");
        }

        var gamePlayer = gamePlayerResult.Value;

        // Get the active game to calculate round-based pricing
        var activeGameResult = await activeGameRepository.GetAsync(gameId, cancellationToken);
        if (!activeGameResult.IsSuccess || activeGameResult.Value is null)
        {
            logger.LogWarning("Purchase failed: GameId={GameId}, PlayerId={PlayerId}, ItemId={ItemId}, Reason=GameNotFound",
                gameId, playerId, itemId);
            return Result.NotFound($"Active game not found with ID: {gameId}");
        }

        var activeGame = activeGameResult.Value;

        // Calculate adjusted price: 15% increase per round
        var adjustedPrice = (int)(item.Price * (1 + 0.15 * activeGame.RoundNumber));

        // Check if player has enough wallet balance
        if (gamePlayer.Wallet < adjustedPrice)
        {
            logger.LogWarning("Purchase failed: PlayerId={PlayerId}, ItemId={ItemId}, Reason=InsufficientFunds, Wallet={Wallet}, Price={Price}",
                playerId, itemId, gamePlayer.Wallet, adjustedPrice);
            return Result.Invalid(new ValidationError($"Insufficient funds. Player wallet: {gamePlayer.Wallet}, Item price: {adjustedPrice}"));
        }

        // Deduct the price from wallet
        gamePlayer.Wallet -= adjustedPrice;

        // Add the item to purchased items (wagers will be accessible via ActiveWagers property)
        gamePlayer.PurchasedItems.Add(item);

        // Update the game player
        var updateResult = await gamePlayerRepository.UpdateAsync(playerId, gamePlayer, cancellationToken);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update game player.");

        logger.LogInformation(
            "Item purchased: GameId={GameId}, PlayerId={PlayerId}, ItemId={ItemId}, ItemName={ItemName}, Price={Price}, Round={Round}",
            gameId, playerId, itemId, item.Name, adjustedPrice, activeGame.RoundNumber);

        return Result.Success(new ShopItemDTO
        {
            Item = item,
            AdjustedPrice = adjustedPrice
        });
    }
}
