using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Shared;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

public interface IItemRepository
{
    Result<ItemBase> Get(string id);
    Result<IEnumerable<ItemBase>> GetAll();
    Result<IEnumerable<ItemBase>> GetRandomNumber(int count = 3);
}
