namespace Microsoft.AspNetCore.Components;

public static class NavigationManagerExtensions
{
    public static void NavigateToLobby(this NavigationManager navManager)
    {
        navManager.NavigateTo("/", replace: true);
    }

    public static void NavigateToLobbyWithGameResult(this NavigationManager navManager, string gameResult)
    {
        navManager.NavigateTo($"/?gameresult={gameResult}", replace: true);
    }

    public static void NavigateToGameplay(this NavigationManager navManager, string gameId)
    {
        navManager.NavigateTo($"/multi-gameplay?gameid={gameId}", replace: true);
    }

    public static bool IsOnGameplay(this NavigationManager navManager)
    {
        return navManager.Uri.Contains("gameplay", StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool IsOnMultiGameplay(this NavigationManager navManager)
    {
        return navManager.Uri.Contains("multi-gameplay", StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool IsOnSoloGameplay(this NavigationManager navManager)
    {
        return navManager.Uri.Contains("solo-gameplay", StringComparison.InvariantCultureIgnoreCase);
    }

    public static void NavigateToSoloGameplay(this NavigationManager navManager)
    {
        navManager.NavigateTo("/solo-gameplay", replace: true);
    }
}
