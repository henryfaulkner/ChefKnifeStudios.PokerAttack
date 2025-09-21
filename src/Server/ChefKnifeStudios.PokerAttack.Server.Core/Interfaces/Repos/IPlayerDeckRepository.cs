using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;

public interface IPlayerDeckRepository
{
    Task AddDeckAsync(string playerId, Deck deck, CancellationToken ct = default);
    Task<Deck?> GetDeckAsync(string playerId, CancellationToken ct = default);
    Task UpdateDeckAsync(string playerId, Deck deck, CancellationToken ct = default);
    Task DeleteDeckAsync(string playerId, CancellationToken ct = default);
}
