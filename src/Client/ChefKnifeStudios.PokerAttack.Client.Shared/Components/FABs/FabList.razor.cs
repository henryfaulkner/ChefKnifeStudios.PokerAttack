using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.FABs;

public partial class FabList : ComponentBase
{
    [Parameter] public FABs[] Fabs { get; set; } = [];
    [Parameter] public Corner Position { get; set; } = Corner.BottomRight;
    [Parameter] public StackDirection Direction { get; set; } = StackDirection.Vertical;
    [Parameter] public Corner? MobilePosition { get; set; }
    [Parameter] public StackDirection? MobileDirection { get; set; }

    public enum FABs
    {
        Settings,
        Help,
    }

    public enum Corner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    public enum StackDirection
    {
        Vertical,
        Horizontal,
    }

    string GetPositionClass() => Position switch
    {
        Corner.TopLeft => "fab-list--top-left",
        Corner.TopRight => "fab-list--top-right",
        Corner.BottomLeft => "fab-list--bottom-left",
        _ => "fab-list--bottom-right"
    };

    string GetDirectionClass() => Direction switch
    {
        StackDirection.Horizontal => "fab-list--horizontal",
        _ => "fab-list--vertical"
    };

    string GetMobilePositionClass() => MobilePosition switch
    {
        Corner.TopLeft => "fab-list--mobile-top-left",
        Corner.TopRight => "fab-list--mobile-top-right",
        Corner.BottomLeft => "fab-list--mobile-bottom-left",
        Corner.BottomRight => "fab-list--mobile-bottom-right",
        _ => ""
    };

    string GetMobileDirectionClass() => MobileDirection switch
    {
        StackDirection.Horizontal => "fab-list--mobile-horizontal",
        StackDirection.Vertical => "fab-list--mobile-vertical",
        _ => ""
    };
}
