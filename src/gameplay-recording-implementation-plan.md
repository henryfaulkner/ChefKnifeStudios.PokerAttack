# Gameplay Recording Implementation Plan
## Strategy 1: Client-Side Browser Recording with MediaRecorder API

**Version:** 1.0
**Date:** 2025-12-14
**Purpose:** Development and playtesting bug reporting tool

---

## Overview

Implement a "record incident" button in the global UI that captures 15 seconds before and 15 seconds after the button press (30 seconds total) of gameplay footage. The recording will be automatically uploaded to Azure Blob Storage for review by developers.

---

## Architecture

### Client-Side (Blazor WASM)
- **Continuous Recording:** MediaRecorder API records gameplay continuously in background
- **Circular Buffer:** Maintains rolling 15-20 second buffer of video chunks
- **On-Demand Capture:** Button press triggers saving of buffered content + 15s future recording
- **Upload:** Completed video blob uploaded to backend API

### Server-Side (.NET Core)
- **Upload Endpoint:** Receives video file from client
- **Azure Integration:** Generates SAS tokens or uploads directly to Blob Storage
- **Metadata Storage:** Tracks recording metadata (timestamp, user, session ID, optional description)

### Azure Resources
- **Azure Blob Storage:** Primary storage for video files
- **Container:** Dedicated container for gameplay recordings (e.g., `gameplay-recordings`)
- **Optional:** Azure Media Services for post-processing/compression

---

## Technical Implementation Details

### 1. Client-Side Components

#### A. Recording Service (JavaScript Interop)
Create a JavaScript module for MediaRecorder functionality:

**Key Responsibilities:**
- Initialize MediaRecorder with canvas stream
- Manage circular buffer of video chunks
- Handle start/stop/save operations
- Generate final video blob

**Technical Specs:**
- **Video Source:** `HTMLCanvasElement.captureStream(30)` - 30 FPS
- **Codec:** VP8/VP9 (WebM) or H.264 (MP4) based on browser support
- **Buffer Strategy:** Keep last 15-20 seconds of chunks, discard older
- **Chunk Duration:** 1 second chunks (timeslice: 1000ms)

**Pseudocode:**
```javascript
class GameplayRecorder {
  constructor(canvasElement) {
    this.stream = canvasElement.captureStream(30);
    this.mediaRecorder = new MediaRecorder(this.stream, {
      mimeType: 'video/webm;codecs=vp9',
      videoBitsPerSecond: 2500000 // 2.5 Mbps
    });
    this.chunks = [];
    this.maxBufferDuration = 20000; // 20 seconds
    this.isRecordingIncident = false;
  }

  startContinuousRecording() {
    this.mediaRecorder.ondataavailable = (e) => {
      this.chunks.push({
        data: e.data,
        timestamp: Date.now()
      });
      this.pruneOldChunks();
    };
    this.mediaRecorder.start(1000); // 1 second chunks
  }

  pruneOldChunks() {
    const cutoff = Date.now() - this.maxBufferDuration;
    this.chunks = this.chunks.filter(c => c.timestamp > cutoff);
  }

  async captureIncident() {
    if (this.isRecordingIncident) return;

    this.isRecordingIncident = true;
    const bufferedChunks = [...this.chunks];

    // Record for 15 more seconds
    await this.recordFutureSegment(15000);

    // Combine buffered + future chunks
    const allChunks = bufferedChunks.concat(this.futureChunks);
    const blob = new Blob(
      allChunks.map(c => c.data),
      { type: 'video/webm' }
    );

    this.isRecordingIncident = false;
    return blob;
  }

  recordFutureSegment(duration) {
    return new Promise((resolve) => {
      this.futureChunks = [];
      const tempRecorder = new MediaRecorder(this.stream);

      tempRecorder.ondataavailable = (e) => {
        this.futureChunks.push({ data: e.data, timestamp: Date.now() });
      };

      tempRecorder.onstop = () => resolve();

      tempRecorder.start(1000);
      setTimeout(() => tempRecorder.stop(), duration);
    });
  }
}
```

#### B. Blazor Component Integration

**RecordingService.cs** (C# wrapper for JS interop):
```csharp
public class RecordingService : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask;

    public RecordingService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "./js/gameplayRecorder.js").AsTask());
    }

    public async Task InitializeAsync(ElementReference canvasElement)
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("initialize", canvasElement);
    }

    public async Task<byte[]> CaptureIncidentAsync()
    {
        var module = await _moduleTask.Value;
        var blob = await module.InvokeAsync<IJSStreamReference>("captureIncident");

        using var stream = await blob.OpenReadStreamAsync(maxAllowedSize: 50_000_000); // 50MB max
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (_moduleTask.IsValueCreated)
        {
            var module = await _moduleTask.Value;
            await module.DisposeAsync();
        }
    }
}
```

#### C. UI Button Component

**RecordIncidentButton.razor:**
```razor
@inject RecordingService RecordingService
@inject HttpClient Http

<button class="record-incident-btn"
        @onclick="RecordIncident"
        disabled="@isRecording">
    @if (isRecording)
    {
        <span>Recording... (@secondsRemaining s)</span>
    }
    else
    {
        <span>🎥 Record Incident</span>
    }
</button>

@code {
    private bool isRecording = false;
    private int secondsRemaining = 15;

    private async Task RecordIncident()
    {
        try
        {
            isRecording = true;

            // Show countdown for remaining recording time
            var timer = new System.Timers.Timer(1000);
            timer.Elapsed += (s, e) =>
            {
                secondsRemaining--;
                InvokeAsync(StateHasChanged);
                if (secondsRemaining <= 0) timer.Stop();
            };
            timer.Start();

            // Capture video
            var videoData = await RecordingService.CaptureIncidentAsync();

            timer.Stop();

            // Upload to server
            await UploadRecording(videoData);

            // Show success message
            // TODO: Implement notification system
        }
        catch (Exception ex)
        {
            // TODO: Log error and show user message
        }
        finally
        {
            isRecording = false;
            secondsRemaining = 15;
        }
    }

    private async Task UploadRecording(byte[] videoData)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(videoData), "video", $"incident_{DateTime.UtcNow:yyyyMMddHHmmss}.webm");

        var response = await Http.PostAsync("/api/recordings/upload", content);
        response.EnsureSuccessStatusCode();
    }
}
```

---

### 2. Server-Side Components

#### A. Recording Upload Controller

**RecordingsController.cs:**
```csharp
[ApiController]
[Route("api/recordings")]
public class RecordingsController : ControllerBase
{
    private readonly IBlobStorageService _blobService;
    private readonly IRecordingMetadataRepository _metadataRepo;
    private readonly ILogger<RecordingsController> _logger;

    [HttpPost("upload")]
    [RequestSizeLimit(52428800)] // 50MB
    public async Task<IActionResult> UploadRecording(IFormFile video)
    {
        if (video == null || video.Length == 0)
            return BadRequest("No video file provided");

        try
        {
            // Generate unique filename
            var fileName = $"{Guid.NewGuid()}_{video.FileName}";

            // Upload to blob storage
            var blobUrl = await _blobService.UploadAsync(
                containerName: "gameplay-recordings",
                fileName: fileName,
                stream: video.OpenReadStream(),
                contentType: video.ContentType
            );

            // Save metadata
            var metadata = new RecordingMetadata
            {
                Id = Guid.NewGuid(),
                FileName = fileName,
                BlobUrl = blobUrl,
                UploadedAt = DateTime.UtcNow,
                UserId = User.Identity?.Name, // If authenticated
                FileSize = video.Length,
                ContentType = video.ContentType
            };

            await _metadataRepo.SaveAsync(metadata);

            _logger.LogInformation(
                "Gameplay recording uploaded: {FileName} by {User}",
                fileName, metadata.UserId ?? "Anonymous"
            );

            return Ok(new { id = metadata.Id, url = blobUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload gameplay recording");
            return StatusCode(500, "Upload failed");
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetRecordings(
        [FromQuery] DateTime? since = null,
        [FromQuery] int pageSize = 50)
    {
        var recordings = await _metadataRepo.GetRecentAsync(since, pageSize);
        return Ok(recordings);
    }
}
```

#### B. Blob Storage Service

**BlobStorageService.cs:**
```csharp
public interface IBlobStorageService
{
    Task<string> UploadAsync(string containerName, string fileName, Stream stream, string contentType);
}

public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;

    public AzureBlobStorageService(IConfiguration configuration)
    {
        var connectionString = configuration["Azure:Storage:ConnectionString"];
        _blobServiceClient = new BlobServiceClient(connectionString);
    }

    public async Task<string> UploadAsync(
        string containerName,
        string fileName,
        Stream stream,
        string contentType)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        var blobClient = containerClient.GetBlobClient(fileName);

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        };

        await blobClient.UploadAsync(stream, options);

        return blobClient.Uri.ToString();
    }
}
```

#### C. Metadata Model & Repository

**RecordingMetadata.cs:**
```csharp
public class RecordingMetadata
{
    public Guid Id { get; set; }
    public string FileName { get; set; }
    public string BlobUrl { get; set; }
    public DateTime UploadedAt { get; set; }
    public string UserId { get; set; }
    public long FileSize { get; set; }
    public string ContentType { get; set; }
    public string Description { get; set; } // Optional user-provided description
    public string SessionId { get; set; } // Game session identifier
}
```

---

### 3. Azure Configuration

#### Required Azure Resources:
1. **Storage Account**
   - Standard performance tier (sufficient for development)
   - LRS redundancy (local redundant storage)
   - Container: `gameplay-recordings`
   - Access tier: Hot (for frequent access during active development)

2. **Connection String Setup**
   - Store in Azure Key Vault or App Configuration
   - Local development: User Secrets

#### appsettings.json:
```json
{
  "Azure": {
    "Storage": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...",
      "RecordingsContainer": "gameplay-recordings"
    }
  },
  "Recording": {
    "MaxFileSizeBytes": 52428800,
    "RetentionDays": 30
  }
}
```

---

## Implementation Phases

### Phase 1: Core Recording (MVP)
**Goal:** Get basic recording working client-side

1. Create JavaScript GameplayRecorder module
2. Implement Blazor RecordingService with JS interop
3. Add simple button to test recording
4. Verify circular buffer works correctly
5. Test 30-second capture functionality

**Testing:** Manually trigger recording, verify video plays back correctly

### Phase 2: Backend Upload
**Goal:** Save recordings to Azure

1. Set up Azure Storage Account and container
2. Implement BlobStorageService
3. Create RecordingsController upload endpoint
4. Add metadata storage (can start with JSON file, migrate to DB later)
5. Wire up client upload to backend

**Testing:** Record incident, verify file appears in Azure Blob Storage

### Phase 3: UI Integration
**Goal:** Professional UI component in game

1. Design and implement RecordIncidentButton component
2. Add to global UI layout
3. Implement countdown/status indicators
4. Add error handling and user feedback
5. Style to match game aesthetic

**Testing:** Play game, trigger recording during various scenarios

### Phase 4: Polish & Features
**Goal:** Production-ready quality

1. Add optional description/notes field
2. Implement session ID correlation
3. Add recording list viewer (admin panel)
4. Implement auto-cleanup (delete after 30 days)
5. Performance optimization (reduce memory footprint)
6. Add telemetry/monitoring

**Testing:** Full playtesting session with multiple testers

---

## Configuration Options

### Recording Quality Settings:
```javascript
const QUALITY_PRESETS = {
  low: {
    fps: 15,
    bitrate: 1000000, // 1 Mbps
    mimeType: 'video/webm;codecs=vp8'
  },
  medium: {
    fps: 30,
    bitrate: 2500000, // 2.5 Mbps
    mimeType: 'video/webm;codecs=vp9'
  },
  high: {
    fps: 60,
    bitrate: 5000000, // 5 Mbps
    mimeType: 'video/webm;codecs=vp9'
  }
};
```

**Recommended for playtesting:** Medium quality (good balance of file size and quality)

---

## Performance Considerations

### Client-Side Impact:
- **Memory Usage:** ~50-100MB for 20-second buffer (depends on resolution/quality)
- **CPU Impact:** 5-10% during continuous recording (modern browsers optimized)
- **Network:** 5-15MB upload per incident (30 seconds of video)

### Mitigation Strategies:
1. **Lazy Initialization:** Only start recording when user opts-in
2. **Quality Toggle:** Allow users to reduce quality on lower-end devices
3. **Compression:** Use efficient codecs (VP9 > VP8 > H.264)
4. **Throttling:** Limit recordings to 1 per minute to prevent abuse

---

## Browser Compatibility

### Supported Browsers:
- ✅ Chrome/Edge (Chromium): Full support
- ✅ Firefox: Full support
- ✅ Safari 14.1+: Partial support (may need fallback codec)
- ❌ IE11: Not supported

### Fallback Strategy:
```javascript
function getBestCodec() {
  const codecs = [
    'video/webm;codecs=vp9',
    'video/webm;codecs=vp8',
    'video/webm',
    'video/mp4'
  ];

  return codecs.find(codec => MediaRecorder.isTypeSupported(codec)) || '';
}
```

---

## Security Considerations

1. **Authentication:** Require user authentication for uploads (optional for playtesting)
2. **Rate Limiting:** Prevent spam uploads (e.g., max 10 recordings/hour per user)
3. **File Validation:** Verify file type and size server-side
4. **Access Control:** Private blob container, SAS tokens for viewing
5. **Retention Policy:** Auto-delete recordings after X days

---

## Testing Strategy

### Unit Tests:
- BlobStorageService upload/download operations
- RecordingMetadataRepository CRUD operations

### Integration Tests:
- End-to-end upload flow (client → server → blob storage)
- Metadata persistence and retrieval

### Manual Testing Checklist:
- [ ] Recording captures before button press
- [ ] Recording captures after button press
- [ ] Total duration is ~30 seconds
- [ ] Video quality is acceptable
- [ ] Upload completes successfully
- [ ] File appears in Azure Blob Storage
- [ ] Metadata is saved correctly
- [ ] Multiple recordings in quick succession
- [ ] Recording during high game activity (stress test)
- [ ] Low-end device performance
- [ ] Network failure during upload (retry/error handling)

---

## Estimated Costs (Azure)

### Storage:
- **Hot Tier:** $0.0184/GB/month
- **Expected Usage:** ~100 recordings/month × 10MB = 1GB = **$0.02/month**

### Bandwidth:
- **Outbound:** $0.087/GB (if developers download videos)
- **Expected:** Minimal (just uploads, viewing in Azure Portal)

**Total Monthly Cost:** < $1 for development/playtesting phase

---

## Future Enhancements

1. **Automatic Bug Detection:** Use Azure Video Indexer to detect errors/crashes in frame
2. **Session Replay:** Combine with Strategy 2 (game state) for full debugging
3. **Annotations:** Allow users to draw on frames or add timestamps
4. **Slack/Teams Integration:** Auto-post to dev channel when recording uploaded
5. **Client-Side Compression:** Reduce upload size with FFmpeg.wasm
6. **Multiple Camera Angles:** If game supports spectator mode

---

## References

- [MDN: MediaRecorder API](https://developer.mozilla.org/en-US/docs/Web/API/MediaRecorder)
- [MDN: HTMLCanvasElement.captureStream()](https://developer.mozilla.org/en-US/docs/Web/API/HTMLCanvasElement/captureStream)
- [Azure Blob Storage Documentation](https://docs.microsoft.com/azure/storage/blobs/)
- [Blazor JavaScript Interop](https://docs.microsoft.com/aspnet/core/blazor/javascript-interoperability)

---

## Contact & Questions

For questions about this implementation plan, contact the development team.
