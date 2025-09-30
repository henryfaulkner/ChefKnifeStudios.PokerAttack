using Microsoft.JSInterop;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface IInputJsInterop : IAsyncDisposable
{
    Task RegisterKeyHandlerAsync(Func<string, Task> onKeyPressed);
    Task UnregisterKeyHandlerAsync();
}

public class InputJsInterop : IInputJsInterop, IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask;
    private DotNetObjectReference<InputJsInterop>? _dotNetRef;
    private readonly IJSRuntime _jsRuntime;
    private Func<string, Task>? _onKeyPressed;

    public InputJsInterop(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
        string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? ".";
        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", $"./_content/{assemblyName}/scripts/inputJsInterop.js?g={Guid.NewGuid():N}").AsTask());
    }

    public async Task RegisterKeyHandlerAsync(Func<string, Task> onKeyPressed)
    {
        _onKeyPressed = onKeyPressed;
        _dotNetRef = DotNetObjectReference.Create(this);
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("registerGlobalKeyHandler", _dotNetRef);
    }

    public async Task UnregisterKeyHandlerAsync()
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("unregisterGlobalKeyHandler");
        _dotNetRef?.Dispose();
        _dotNetRef = null;
    }

    [JSInvokable]
    public async Task OnKeyPressed(string key)
    {
        if (_onKeyPressed is not null)
            await _onKeyPressed.Invoke(key);
    }

    public async ValueTask DisposeAsync()
    {
        await UnregisterKeyHandlerAsync();
    }
}
