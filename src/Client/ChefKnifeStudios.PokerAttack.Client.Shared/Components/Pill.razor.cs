using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components;

public enum PillTypes
{
    Secondary,
    Primary,
    Destructive,
}

public partial class Pill : ComponentBase
{
    [Parameter] public required string Text { get; set; }
    [Parameter] public PillTypes Type { get; set; }
    [Parameter] public EventCallback OnClickCallback { get; set; }
    [Parameter] public string? Icon { get; set; }
    [Parameter] public bool StopPropagation { get; set; } = false;
}

public static class PillHelper
{
    public static string GetPillCssClass(PillTypes pillType)
    {
        return pillType switch
        {
            PillTypes.Primary => "pill-primary",
            PillTypes.Secondary => "pill-secondary",
            PillTypes.Destructive => "pill-danger",
            _ => string.Empty,
        };
    }
}
