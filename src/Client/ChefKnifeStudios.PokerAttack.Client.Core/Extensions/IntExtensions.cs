namespace ChefKnifeStudios.PokerAttack.Client.Core.Extensions;

public static class IntExtensions
{
    public static string FormatAsMinutesSeconds(this int totalSeconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(totalSeconds);
        return time.ToString(@"mm\:ss");
    }
}
