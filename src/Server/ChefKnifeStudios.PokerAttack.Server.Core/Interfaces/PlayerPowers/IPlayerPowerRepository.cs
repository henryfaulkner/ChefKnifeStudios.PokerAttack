using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure.PlayerPowers;

public interface IPlayerPowerRepository
{
    PlayerPower? Get(string id);
    IEnumerable<PlayerPower> GetAll();
    IEnumerable<PlayerPower> GetRandomNumber(int count = 3);
}
