using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Numerics;
using System.Text.Json.Serialization;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;

public interface ICommonJsInterop
{
    ValueTask DisposeAsync();
    ValueTask<BoundingRect?> GetBoundingRectAsync(string elementId);
    ValueTask<BoundingRect?> GetBoundingRectWithPageOffsetAsync(string elementId);
    ValueTask<string> PromptAsync(string message);
    Task ScrollToTopAsync(string elementId, int? delayMilliseconds = 100);
    Task ScrollToEndAsync(string elementId, int? delayMilliseconds = 100);
    Task ScrollIntoViewAsync(string elementId, int? delayMilliseconds = 100);
    Task SetFocusAsync(string elementId);
    Task SetFocusBySelectorAsync(string cssSelector);
    Task LoseFocusAsync(string elementId);
    Task ClickElementAsync(string selector);
    ValueTask<string> BodyClickEventCallbackAsync(Func<System.EventArgs, Task> callback);
    Task AddClassToElementAsync(string className, string elementId, int milisecondsToHaveClass);
    Task AddClassToAllElementsWithClassAsync(string targetClass, string newClass);
    Task RemoveClassFromAllElementsWithClassAsync(string targetClass, string removedClass);
    Task KeyDownEventCallbackAsync(Func<KeyboardEventArgs, Task> callback);
    Task<int> GetViewPortWidthAsync();
    Task<Vector2> GetViewPortSizeAsync();
    Task<bool> OpenLinkInNewTabAsync(string url);
    Task CopyTextToClipboardAsync(string elementId);
    Task SelectTextForElementByIdAsync(string elementId);
    Task AddOutsideClickListenerAsync(string elementId, Action callback);
    Task RemoveOutsideClickListenerAsync(string elementId);
    Task RemoveAllViaQuerySelectorAsync(string querySelector);
    Task SetThemeAsync(string themeName);
}

public class CommonJsInterop : ICommonJsInterop, IAsyncDisposable
{
    readonly Lazy<Task<IJSObjectReference>> moduleTask;
    readonly ILogger<CommonJsInterop> _logger;

    DotNetObjectReference<InteropEventHelper>? Reference;

    public CommonJsInterop(
        IJSRuntime jsRuntime, 
        IWebAssemblyHostEnvironment environment, 
        ILogger<CommonJsInterop> logger)
    {
        _logger = logger;
        string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? ".";

        moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", $"./_content/{assemblyName}/scripts/commonJsInterop.js?g={Guid.NewGuid().ToString().ToLower()}").AsTask());
    }

    public async ValueTask DisposeAsync()
    {
        if (moduleTask.IsValueCreated)
        {
            var module = await moduleTask.Value;
            await module.DisposeAsync();
        }
    }

    public async ValueTask<string> PromptAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        try
        {
            var module = await moduleTask.Value;
            return await module.InvokeAsync<string>("showPrompt", message);
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task SetFocusBySelectorAsync(string cssSelector)
    {
        if (string.IsNullOrWhiteSpace(cssSelector)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("setFocusBySelector", cssSelector);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task SetFocusAsync(string elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("setFocus", elementId);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task LoseFocusAsync(string elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("loseFocus", elementId);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task ScrollToTopAsync(string elementId, int? delayMilliseconds = 100)
    {
        if (string.IsNullOrWhiteSpace(elementId)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("scrollToTop", elementId, delayMilliseconds);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task ScrollToEndAsync(string elementId, int? delayMilliseconds = 100)
    {
        if (string.IsNullOrWhiteSpace(elementId)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("scrollToEnd", elementId, delayMilliseconds);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task ScrollIntoViewAsync(string selector, int? delayMilliseconds = 100)
    {
        if (string.IsNullOrWhiteSpace(selector)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("scrollIntoView", selector, delayMilliseconds);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async ValueTask<BoundingRect?> GetBoundingRectAsync(string elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId)) return null;

        try
        {
            var module = await moduleTask.Value;
            return await module.InvokeAsync<BoundingRect?>("getBoundingClientRect", elementId);
        }
        catch (Exception ex)
        {
            LogError(ex);
            return null;
        }
    }

    public async ValueTask<BoundingRect?> GetBoundingRectWithPageOffsetAsync(string elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId)) return null;

        try
        {
            var module = await moduleTask.Value;
            return await module.InvokeAsync<BoundingRect?>("getBoundingClientRectWithPageOffset", elementId);
        }
        catch (Exception ex)
        {
            LogError(ex);
            return null;
        }
    }

    public async Task ClickElementAsync(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("clickElement", selector);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async ValueTask<string> BodyClickEventCallbackAsync(Func<System.EventArgs, Task> callback)
    {
        try
        {
            var module = await moduleTask.Value;

            Reference = DotNetObjectReference.Create(new InteropEventHelper(callback));
            return await module.InvokeAsync<string>("registerBodyClickEvent", Reference);
        }
        catch (Exception ex)
        {
            LogError(ex);
            return string.Empty;
        }
    }

    public async Task AddClassToElementAsync(string className, string elementId, int milisecondsToHaveClass = 1000)
    {
        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("addTemporaryClassToElementById", className, elementId, milisecondsToHaveClass);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task AddClassToAllElementsWithClassAsync(string targetClass, string newClass)
    {
        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("addClassToAllElementsWithClass", targetClass, newClass);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task RemoveClassFromAllElementsWithClassAsync(string targetClass, string removedClass)
    {
        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("removeClassFromAllElementsWithClass", targetClass, removedClass);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task KeyDownEventCallbackAsync(Func<KeyboardEventArgs, Task> callback)
    {
        try
        {
            var module = await moduleTask.Value;

            Reference = DotNetObjectReference.Create(new InteropEventHelper(callback));
            await module.InvokeVoidAsync("registerKeyDownEvent", Reference);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task<int> GetViewPortWidthAsync()
    {
        try
        {
            var module = await moduleTask.Value;

            int width = await module.InvokeAsync<int>("returnViewportWidth");
            return width;
        }
        catch (Exception ex) { LogError(ex); }

        return 0;
    }

    struct ViewportSizeJson
    {
        [JsonPropertyName("x")]
        public float X { get; set; }
        [JsonPropertyName("y")]
        public float Y { get; set; }
    }
    public async Task<Vector2> GetViewPortSizeAsync()
    {
        try
        {
            var module = await moduleTask.Value;

            var size = await module.InvokeAsync<ViewportSizeJson>("getViewportSize");
            return new Vector2(size.X, size.Y);
        }
        catch (Exception ex) { LogError(ex); }

        return Vector2.Zero;
    }

    public async Task<bool> OpenLinkInNewTabAsync(string url)
    {
        try
        {
            var module = await moduleTask.Value;
            bool popupBlocked = await module.InvokeAsync<bool>("openLinkInNewTab", url);

            bool success = !popupBlocked;
            return success;
        }
        catch (Exception ex) { LogError(ex); }

        return false;
    }

    public async Task CopyTextToClipboardAsync(string elementId)
    {
        try
        {
            var module = await moduleTask.Value;

            await module.InvokeVoidAsync("copyToClipboard", elementId);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task SelectTextForElementByIdAsync(string elementId)
    {
        try
        {
            var module = await moduleTask.Value;

            await module.InvokeVoidAsync("selectInput", elementId);
        }
        catch (Exception ex) { LogError(ex); }
    }

    readonly Dictionary<string, (Action Callback, object? Listener)> _outsideClickCallbackDict = new();
    public async Task AddOutsideClickListenerAsync(string elementId, Action callback)
    {
        try
        {
            var module = await moduleTask.Value;

            // Add the listener via JavaScript and store the listener reference
            var listener = await module.InvokeAsync<object>(
                "addOutsideClickListener",
                elementId,
                DotNetObjectReference.Create(this)
            );

            _outsideClickCallbackDict[elementId] = (callback, listener);
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    [JSInvokable]
    public void HandleOutsideClick(string elementId)
    {
        if (_outsideClickCallbackDict.TryGetValue(elementId, out var callbackData))
        {
            callbackData.Callback.Invoke();
        }
    }

    public async Task RemoveOutsideClickListenerAsync(string elementId)
    {
        try
        {
            if (_outsideClickCallbackDict.TryGetValue(elementId, out var callbackData) && callbackData.Listener is not null)
            {
                var module = await moduleTask.Value;

                // Remove the specific listener in JavaScript
                await module.InvokeVoidAsync("removeOutsideClickListener", callbackData.Listener);

                // Clean up the dictionary
                _outsideClickCallbackDict.Remove(elementId);
            }
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    public async Task RemoveAllViaQuerySelectorAsync(string querySelector)
    {
        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("removeAllViaQuerySelector", querySelector);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task SetThemeAsync(string themeName)
    {
        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("setTheme", themeName);
        }
        catch (Exception ex) { LogError(ex); }
    }

    void LogError(Exception ex)
    {
        _logger.LogError(
            ex,
            "CommonInterop encountered a JavaScript error: {errorNessage}",
            ex.Message);
    }
}

public class InteropEventHelper
{
    private readonly Func<System.EventArgs, Task>? _callback;
    private readonly Func<KeyboardEventArgs, Task>? _keyDownCallback;

    public InteropEventHelper(Func<KeyboardEventArgs, Task> keyDownCallback)
    {
        _keyDownCallback = keyDownCallback;
    }

    public InteropEventHelper(Func<System.EventArgs, Task> callback)
    {
        _callback = callback;
    }

    [JSInvokable]
    public Task OnBodyClick(System.EventArgs? args)
    {
        if (_callback != null) return _callback(args!);

        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnKeyDown(KeyboardEventArgs? args)
    {
        if (_keyDownCallback != null) return _keyDownCallback(args!);

        return Task.CompletedTask;
    }
}

public struct BoundingRect
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("width")]
    public float Width { get; set; }

    [JsonPropertyName("height")]
    public float Height { get; set; }

    [JsonPropertyName("top")]
    public float Top { get; set; }

    [JsonPropertyName("right")]
    public float Right { get; set; }

    [JsonPropertyName("bottom")]
    public float Bottom { get; set; }

    [JsonPropertyName("left")]
    public float Left { get; set; }
}
