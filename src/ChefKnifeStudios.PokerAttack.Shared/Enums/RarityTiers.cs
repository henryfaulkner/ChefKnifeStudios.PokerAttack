using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Shared.Enums;

public enum RarityTiers
{
    [Description("Common")]
    Common = 1,
    [Description("Uncommon")]
    Uncommon = 2,
    [Description("Rare")]
    Rare = 3,
    [Description("Epic")]
    Epic = 4,
    [Description("Legendary")]
    Legendary = 5,
}