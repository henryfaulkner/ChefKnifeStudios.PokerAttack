using Microsoft.JSInterop;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;

public interface IRecorderInterop : IAsyncDisposable
{
    Task InitializeRecorderAsync(string canvasElementId);
    Task StartContinuousRecordingAsync();
    Task<byte[]?> CaptureIncidentAsync();
    Task StopRecordingAsync();
}

public class RecorderInterop : IRecorderInterop, IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask;
    private readonly IJSRuntime _jsRuntime;

    public RecorderInterop(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
        string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? ".";
        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", $"./_content/{assemblyName}/scripts/recorderJsInterop.js?g={Guid.NewGuid():N}").AsTask());
    }

    public async Task InitializeRecorderAsync(string canvasElementId)
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("initializeRecorder", canvasElementId);
    }

    public async Task StartContinuousRecordingAsync()
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("startContinuousRecording");
    }

    public async Task<byte[]?> CaptureIncidentAsync()
    {
        var module = await _moduleTask.Value;
        var base64Video = await module.InvokeAsync<string?>("captureIncident");
        
        if (string.IsNullOrEmpty(base64Video))
            return null;

        return Convert.FromBase64String(base64Video);
    }

    public async Task StopRecordingAsync()
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("stopRecording");
    }

    public async ValueTask DisposeAsync()
    {
        if (_moduleTask.IsValueCreated)
        {
            await StopRecordingAsync();
            var module = await _moduleTask.Value;
            await module.DisposeAsync();
        }
    }
}