using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;

public interface IItemRepository
{
    ItemBase? Get(string id);
    IEnumerable<ItemBase> GetAll();
    IEnumerable<ItemBase> GetRandomNumber(int count = 3);
}
