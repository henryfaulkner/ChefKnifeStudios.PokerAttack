using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;

public interface IAudioJsInterop
{
    Task PlayOneShotAsync(string audioFilePath);
}

public class AudioJsInterop : IAudioJsInterop, IAsyncDisposable
{
    readonly Lazy<Task<IJSObjectReference>> moduleTask;
    readonly ILogger<AudioJsInterop> _logger;

    public AudioJsInterop(
        IJSRuntime jsRuntime,
        IWebAssemblyHostEnvironment environment,
        ILogger<AudioJsInterop> logger)
    {
        _logger = logger;
        string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? ".";

        moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", $"./_content/{assemblyName}/scripts/audioJsInterop.js?g={Guid.NewGuid().ToString().ToLower()}").AsTask());
    }

    public async ValueTask DisposeAsync()
    {
        if (moduleTask.IsValueCreated)
        {
            var module = await moduleTask.Value;
            await module.DisposeAsync();
        }
    }

    public async Task PlayOneShotAsync(string audioFilePath)
    {
        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("playOneShot", audioFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing audio file: {AudioFilePath}", audioFilePath);
        }
    }
}
