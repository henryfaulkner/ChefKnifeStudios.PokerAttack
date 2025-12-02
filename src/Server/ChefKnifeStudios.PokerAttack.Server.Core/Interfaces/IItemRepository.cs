using ChefKnifeStudios.PokerAttack.Shared;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

public interface IItemRepository
{
    ItemBase? Get(string id);
    IEnumerable<ItemBase> GetAll();
    IEnumerable<ItemBase> GetRandomNumber(int count = 3);
}
