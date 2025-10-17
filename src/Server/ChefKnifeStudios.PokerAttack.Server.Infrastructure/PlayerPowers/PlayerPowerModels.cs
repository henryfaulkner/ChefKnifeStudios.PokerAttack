using ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class PlayerPower
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int PointCost { get; set; }
    public List<PowerEffectInstance> Effects { get; set; } = new();
}

public class PowerEffectInstance
{
    public string Type { get; set; }  // effect ID
    public Dictionary<string, string>? Parameters { get; set; }
}

public class PowerService
{
    public void Activate(GamePlayer player, PlayerPower power)
    {
        if (player.PowerPoints < power.PointCost)
            throw new ApplicationException("Not enough PP!");

        player.PowerPoints -= power.PointCost;

        foreach (var effectInstance in power.Effects)
        {
            var effect = PowerEffectRegistry.Get(effectInstance.Type);
            effect.Apply(player, effectInstance.Parameters);
        }
    }
}

public static class PowerEffectRegistry
{
    private static readonly Dictionary<string, IPowerEffect> _effects = new()
    {
        { "flip", new FlipCardEffect() },
        { "changeSuit", new ChangeSuitEffect() }
    };

    public static IPowerEffect Get(string effectId)
    {
        return _effects[effectId];
    }
}

public interface IPowerEffect
{
    void Apply(GamePlayer player, Dictionary<string, string>? parameters);
}

public class FlipCardEffect : IPowerEffect
{
    public void Apply(GamePlayer player, Dictionary<string, string>? parameters)
    {
        int count = int.Parse(parameters?["count"] ?? "1");
        //player.FlipRandomCards(count);
    }
}

public class ChangeSuitEffect : IPowerEffect
{
    public void Apply(GamePlayer player, Dictionary<string, string>? parameters)
    {
        string suit = parameters?["suit"] ?? "Hearts";
        //player.ChangeRandomCardSuit(suit);
    }
}