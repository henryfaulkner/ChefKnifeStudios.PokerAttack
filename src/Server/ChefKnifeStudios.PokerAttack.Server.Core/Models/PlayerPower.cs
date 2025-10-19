using System.Text.Json.Serialization;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public class PlayerPower
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public required string Description { get; set; }

    [JsonPropertyName("powerKind")]
    public PowerKind PowerKind { get; set; }

    [JsonPropertyName("pointCost")]
    public int PointCost { get; set; }

    [JsonPropertyName("effects")]
    public List<PowerEffectInstance> Effects { get; set; } = new();
}

public class PowerEffectInstance
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }  // effect ID

    [JsonPropertyName("target")]
    public PowerTarget Target { get; set; } = PowerTarget.Self;

    [JsonPropertyName("trigger")]
    public PowerTrigger Trigger { get; set; } = PowerTrigger.None;

    [JsonPropertyName("parameters")]
    public Dictionary<string, string>? Parameters { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PowerTarget
{
    [JsonPropertyName("self")]
    Self,

    [JsonPropertyName("opponent")]
    Opponent
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PowerKind
{
    [JsonPropertyName("active")]
    Active,

    [JsonPropertyName("passive")]
    Passive
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PowerTrigger
{
    [JsonPropertyName("none")]
    None, // For active powers

    [JsonPropertyName("onHandPlayed")]
    OnHandPlayed
}
