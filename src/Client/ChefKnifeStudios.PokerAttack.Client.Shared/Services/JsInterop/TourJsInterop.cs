using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;

public interface ITourJsInterop
{
    ValueTask DisposeAsync();
    Task HighlightElementAsync(string selector, string title, string description);
    Task StartTourAsync(IEnumerable<TourStep> steps);
    Task DestroyTourAsync();
}

public record TourStep(
    string Element,
    string Title,
    string Description,
    string? Side = "bottom",
    string? Align = "center"
);

public class TourJsInterop : ITourJsInterop, IAsyncDisposable
{
    readonly Lazy<Task<IJSObjectReference>> _moduleTask;
    readonly ILogger<TourJsInterop> _logger;

    public TourJsInterop(
        IJSRuntime jsRuntime,
        IWebAssemblyHostEnvironment environment,
        ILogger<TourJsInterop> logger)
    {
        _logger = logger;
        string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? ".";

        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", $"./_content/{assemblyName}/scripts/tourJsInterop.js?g={Guid.NewGuid().ToString().ToLower()}").AsTask());
    }

    public async ValueTask DisposeAsync()
    {
        if (_moduleTask.IsValueCreated)
        {
            try
            {
                var module = await _moduleTask.Value;
                await module.InvokeVoidAsync("destroyTour");
                await module.DisposeAsync();
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }
    }

    public async Task HighlightElementAsync(string selector, string title, string description)
    {
        if (string.IsNullOrWhiteSpace(selector)) return;

        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("highlightElement", selector, title, description);
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    public async Task StartTourAsync(IEnumerable<TourStep> steps)
    {
        if (steps == null || !steps.Any()) return;

        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("startTour", steps);
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    public async Task DestroyTourAsync()
    {
        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("destroyTour");
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
            "TourJsInterop encountered a JavaScript error: {errorMessage}",
            ex.Message);
    }
}
