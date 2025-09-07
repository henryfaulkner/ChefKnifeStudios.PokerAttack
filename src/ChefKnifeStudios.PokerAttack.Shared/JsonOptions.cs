using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChefKnifeStudios.PokerAttack.Shared;

public static class JsonOptions
{
    public static JsonSerializerOptions Get() => new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
