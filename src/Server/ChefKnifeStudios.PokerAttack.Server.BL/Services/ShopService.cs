using ChefKnifeStudios.PokerAttack.Shared.DTOs;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IShopService
{
    Task<IEnumerable<ShopItemDTO>> GetShopItemsAsync(CancellationToken cancellationToken = default);
    Task<ShopItemDTO> PurchaseItemAsync(string gameId, string playerId, string itemId, CancellationToken cancellationToken = default);

}

public class ShopService : IShopService
{
    public async Task<IEnumerable<ShopItemDTO>> GetShopItemsAsync(CancellationToken cancellationToken = default)
    {
        // Implementation goes here
        throw new NotImplementedException();
    }

    public async Task<ShopItemDTO> PurchaseItemAsync(string gameId, string playerId, string itemId, CancellationToken cancellationToken = default)
    {
        // Implementation goes here
        throw new NotImplementedException();
    }
}
