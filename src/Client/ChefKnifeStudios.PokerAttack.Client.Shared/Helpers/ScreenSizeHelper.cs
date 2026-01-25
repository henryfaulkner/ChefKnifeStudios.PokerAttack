using System.Numerics;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Helpers;

public enum ScreenSizeBreakpoint
{
    MobilePortrait,
    MobileLandscape,
    Tablet,
    Desktop,
    LargeDesktop
}

public static class ScreenSizeHelper
{
    public const int MobileMaxWidth = 480;
    public const int MobileMaxHeight = 896;
    public const int TabletMaxWidth = 768;
    public const int TabletMaxHeight = 1024;
    public const int DesktopMaxWidth = 1024;
    public const int LargeDesktopMinWidth = 1025;

    public static ScreenSizeBreakpoint GetBreakpoint(Vector2 screenSize)
    {
        var width = screenSize.X;
        var height = screenSize.Y;

        var isPortrait = height > width;
        var smallerDimension = Math.Min(width, height);
        var largerDimension = Math.Max(width, height);

        // Mobile: smaller dimension <= 480, larger dimension <= 896
        var isMobileSize = smallerDimension <= MobileMaxWidth && largerDimension <= MobileMaxHeight;

        // Tablet: smaller dimension <= 768, larger dimension <= 1024
        var isTabletSize = smallerDimension <= TabletMaxWidth && largerDimension <= TabletMaxHeight;

        return (isMobileSize, isTabletSize, isPortrait, width) switch
        {
            (true, _, true, _) => ScreenSizeBreakpoint.MobilePortrait,
            (true, _, false, _) => ScreenSizeBreakpoint.MobileLandscape,
            (false, true, _, _) => ScreenSizeBreakpoint.Tablet,
            (false, false, _, <= DesktopMaxWidth) => ScreenSizeBreakpoint.Desktop,
            _ => ScreenSizeBreakpoint.LargeDesktop
        };
    }

    public static ScreenSizeBreakpoint GetBreakpoint(float width, float height)
        => GetBreakpoint(new Vector2(width, height));

    public static bool IsMobile(Vector2 screenSize)
        => GetBreakpoint(screenSize) is ScreenSizeBreakpoint.MobilePortrait or ScreenSizeBreakpoint.MobileLandscape;

    public static bool IsTablet(Vector2 screenSize)
        => GetBreakpoint(screenSize) == ScreenSizeBreakpoint.Tablet;

    public static bool IsDesktop(Vector2 screenSize)
        => GetBreakpoint(screenSize) is ScreenSizeBreakpoint.Desktop or ScreenSizeBreakpoint.LargeDesktop;

    public static bool IsPortrait(Vector2 screenSize)
        => screenSize.Y > screenSize.X;
}
