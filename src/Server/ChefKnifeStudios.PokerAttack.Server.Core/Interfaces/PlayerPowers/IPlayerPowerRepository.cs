using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure.PlayerPowers;

public interface IPlayerPowerRepository
{
    Result<PlayerPower> Get(string id);
    Result<IEnumerable<PlayerPower>> GetAll();
    Result<IEnumerable<PlayerPower>> GetRandomNumber(int count = 3);
}
