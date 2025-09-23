using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface ILobbyJsInterop
{
    Task RegisterNotifyServerOnUnloadAsync(string url, object data);
    Task TestNotifyServerAsync(string url, object data);
}

public class LobbyJsInterop : ILobbyJsInterop, IAsyncDisposable
{
    readonly Lazy<Task<IJSObjectReference>> moduleTask;
    readonly ILogger<LobbyJsInterop> _logger;

    public LobbyJsInterop(
        IJSRuntime jsRuntime,
        IWebAssemblyHostEnvironment environment,
        ILogger<LobbyJsInterop> logger)
    {
        _logger = logger;
        string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? ".";

        moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", $"./_content/{assemblyName}/scripts/lobbyJsInterop.js?g={Guid.NewGuid().ToString().ToLower()}").AsTask());
    }

    public async ValueTask DisposeAsync()
    {
        if (moduleTask.IsValueCreated)
        {
            var module = await moduleTask.Value;
            await module.DisposeAsync();
        }
    }

    public async Task RegisterNotifyServerOnUnloadAsync(string url, object data)
    {
        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("notifyServerOnUnload", url, data);
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    public async Task TestNotifyServerAsync(string url, object data)
    {
        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("testNotifyServer", url, data);
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    void LogError(Exception ex)
    {
        _logger.LogError(
            ex,
            "LobbyJsInterop encountered a JavaScript error: {errorNessage}",
            ex.Message);
    }
}
