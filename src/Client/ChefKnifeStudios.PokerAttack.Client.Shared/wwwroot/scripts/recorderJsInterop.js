let recorderInstance = null;

export function initializeRecorder(canvasElementId) {
  const canvas = document.getElementById(canvasElementId);
  if (!canvas) {
    throw new Error(`Canvas element with id '${canvasElementId}' not found`);
  }
  recorderInstance = new GameplayRecorder(canvas);
}

export function startContinuousRecording() {
  if (!recorderInstance) {
    throw new Error('Recorder not initialized. Call initializeRecorder first.');
  }
  recorderInstance.startContinuousRecording();
}

export async function captureIncident() {
  if (!recorderInstance) {
    throw new Error('Recorder not initialized.');
  }
  
  const blob = await recorderInstance.captureIncident();
  if (!blob) return null;

  // Convert blob to base64 for transfer to C#
  return await blobToBase64(blob);
}

export function stopRecording() {
  if (recorderInstance) {
    recorderInstance.stop();
    recorderInstance = null;
  }
}

// Helper function to convert blob to base64
function blobToBase64(blob) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onloadend = () => {
      const base64 = reader.result.split(',')[1];
      resolve(base64);
    };
    reader.onerror = reject;
    reader.readAsDataURL(blob);
  });
}

// Your original GameplayRecorder class
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
    if (this.isRecordingIncident) return null;

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

  stop() {
    if (this.mediaRecorder && this.mediaRecorder.state !== 'inactive') {
      this.mediaRecorder.stop();
    }
  }
}