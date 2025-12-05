using ChefKnifeStudios.PokerAttack.Shared;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface IShopService
{
    Task<IEnumerable<ItemBase>> GetShopItemsAsync(CancellationToken cancellationToken = default);
    Task<ItemBase> PurchaseItemAsync(string gameId, string playerId, string itemId, CancellationToken cancellationToken = default);

}

public class ShopService : IShopService
{
    public async Task<IEnumerable<ItemBase>> GetShopItemsAsync(CancellationToken cancellationToken = default)
    {
        // Implementation goes here
        throw new NotImplementedException();
    }

    public async Task<ItemBase> PurchaseItemAsync(string gameId, string playerId, string itemId, CancellationToken cancellationToken = default)
    {
        // Implementation goes here
        throw new NotImplementedException();
    }
}
