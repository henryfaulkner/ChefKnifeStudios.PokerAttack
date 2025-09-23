using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Numerics;
using System.Text.Json.Serialization;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;
public interface ICommonJsInterop
{
    ValueTask DisposeAsync();
    ValueTask<BoundingRect?> GetBoundingRect(string elementId);
    ValueTask<BoundingRect?> GetBoundingRectWithPageOffset(string elementId);
    ValueTask<string> Prompt(string message);
    Task ScrollToTop(string elementId, int? delayMilliseconds = 100);
    Task ScrollToEnd(string elementId, int? delayMilliseconds = 100);
    Task ScrollIntoView(string elementId, int? delayMilliseconds = 100);
    Task SetFocus(string elementId);
    Task SetFocusBySelector(string cssSelector);
    Task LoseFocus(string elementId);
    Task ClickElement(string selector);
    ValueTask<string> BodyClickEventCallback(Func<System.EventArgs, Task> callback);
    Task AddClassToElement(string className, string elementId, int milisecondsToHaveClass);
    Task AddClassToAllElementsWithClass(string targetClass, string newClass);
    Task RemoveClassFromAllElementsWithClass(string targetClass, string removedClass);
    Task KeyDownEventCallback(Func<KeyboardEventArgs, Task> callback);
    Task<int> GetViewPortWidth();
    Task<Vector2> GetViewPortSize();
    Task<bool> OpenLinkInNewTab(string url);
    Task CopyTextToClipboard(string elementId);
    Task SelectTextForElementById(string elementId);
    Task AddOutsideClickListener(string elementId, Action callback);
    Task RemoveOutsideClickListener(string elementId);
    Task RemoveAllViaQuerySelector(string querySelector);
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

    public async ValueTask<string> Prompt(string message)
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

    public async Task SetFocusBySelector(string cssSelector)
    {
        if (string.IsNullOrWhiteSpace(cssSelector)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("setFocusBySelector", cssSelector);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task SetFocus(string elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("setFocus", elementId);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task LoseFocus(string elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("loseFocus", elementId);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task ScrollToTop(string elementId, int? delayMilliseconds = 100)
    {
        if (string.IsNullOrWhiteSpace(elementId)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("scrollToTop", elementId, delayMilliseconds);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task ScrollToEnd(string elementId, int? delayMilliseconds = 100)
    {
        if (string.IsNullOrWhiteSpace(elementId)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("scrollToEnd", elementId, delayMilliseconds);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task ScrollIntoView(string selector, int? delayMilliseconds = 100)
    {
        if (string.IsNullOrWhiteSpace(selector)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("scrollIntoView", selector, delayMilliseconds);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async ValueTask<BoundingRect?> GetBoundingRect(string elementId)
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

    public async ValueTask<BoundingRect?> GetBoundingRectWithPageOffset(string elementId)
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

    public async Task ClickElement(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return;

        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("clickElement", selector);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async ValueTask<string> BodyClickEventCallback(Func<System.EventArgs, Task> callback)
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

    public async Task AddClassToElement(string className, string elementId, int milisecondsToHaveClass = 1000)
    {
        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("addTemporaryClassToElementById", className, elementId, milisecondsToHaveClass);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task AddClassToAllElementsWithClass(string targetClass, string newClass)
    {
        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("addClassToAllElementsWithClass", targetClass, newClass);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task RemoveClassFromAllElementsWithClass(string targetClass, string removedClass)
    {
        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("removeClassFromAllElementsWithClass", targetClass, removedClass);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task KeyDownEventCallback(Func<KeyboardEventArgs, Task> callback)
    {
        try
        {
            var module = await moduleTask.Value;

            Reference = DotNetObjectReference.Create(new InteropEventHelper(callback));
            await module.InvokeVoidAsync("registerKeyDownEvent", Reference);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task<int> GetViewPortWidth()
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
    public async Task<Vector2> GetViewPortSize()
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

    public async Task<bool> OpenLinkInNewTab(string url)
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

    public async Task CopyTextToClipboard(string elementId)
    {
        try
        {
            var module = await moduleTask.Value;

            await module.InvokeVoidAsync("copyToClipboard", elementId);
        }
        catch (Exception ex) { LogError(ex); }
    }

    public async Task SelectTextForElementById(string elementId)
    {
        try
        {
            var module = await moduleTask.Value;

            await module.InvokeVoidAsync("selectInput", elementId);
        }
        catch (Exception ex) { LogError(ex); }
    }

    readonly Dictionary<string, (Action Callback, object? Listener)> _outsideClickCallbackDict = new();
    public async Task AddOutsideClickListener(string elementId, Action callback)
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

    public async Task RemoveOutsideClickListener(string elementId)
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

    public async Task RemoveAllViaQuerySelector(string querySelector)
    {
        try
        {
            var module = await moduleTask.Value;
            await module.InvokeVoidAsync("removeAllViaQuerySelector", querySelector);
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
