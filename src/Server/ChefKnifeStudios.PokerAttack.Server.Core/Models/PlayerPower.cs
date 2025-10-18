namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class PlayerPower
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public PowerKind PowerKind { get; set; }
    public int PointCost { get; set; }
    public List<PowerEffectInstance> Effects { get; set; } = new();
}

public class PowerEffectInstance
{
    public required string Type { get; set; }  // effect ID
    public PowerTarget Target { get; set; } = PowerTarget.Self;
    public PowerTrigger Trigger { get; set; } = PowerTrigger.None;
    public Dictionary<string, string>? Parameters { get; set; }
}

public enum PowerTarget
{
    Self,
    Opponent,
}

public enum PowerKind
{
    Active,
    Passive
}

public enum PowerTrigger
{
    None,              // Active powers
    OnHandPlayed,
}