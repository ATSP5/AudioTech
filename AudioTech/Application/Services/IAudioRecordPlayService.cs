using AudioTech.Application.Filters;

namespace AudioTech.Application.Services;

public enum AudioSaveFormat { Wav, Mp3 }

public interface IAudioRecordPlayService : IDisposable
{
    // ── Recording ─────────────────────────────────────────────────────────────

    /// <summary>Start buffering filtered samples from channel 0.</summary>
    void StartRecording(int sampleRate);

    /// <summary>
    /// Seal the buffer and persist it to a temp WAV so playback is possible.
    /// Fires <see cref="StateChanged"/> twice: once immediately (recording
    /// stopped), once when the temp file is ready (play becomes available).
    /// </summary>
    void StopRecording();

    /// <summary>Export the last recording to the given path.</summary>
    Task SaveRecordingAsync(string filePath, AudioSaveFormat format);

    // ── Playback ──────────────────────────────────────────────────────────────

    /// <summary>Load a file; pre-reads its duration. Does not start playback.</summary>
    void LoadFile(string filePath);

    void StartPlayback(FilterType filterType, float filterStrength, EqualizerSettings? equalizerSettings = null);
    void StopPlayback();

    /// <summary>
    /// Replaces the active playback filter on-the-fly. No-op if not playing.
    /// </summary>
    void UpdatePlaybackFilter(FilterType filterType, float filterStrength, EqualizerSettings? equalizerSettings = null);

    /// <summary>Seek to a position expressed as a fraction of total duration [0, 1].</summary>
    void SeekTo(double fraction);

    // ── State ─────────────────────────────────────────────────────────────────

    bool IsRecording  { get; }
    bool IsPlaying    { get; }
    bool HasLoadedFile { get; }

    TimeSpan RecordingDuration { get; }
    TimeSpan PlaybackPosition  { get; }
    TimeSpan PlaybackDuration  { get; }
    string?  LoadedFilePath    { get; }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>FFT frame from playback — marshal to UI thread before use.</summary>
    event EventHandler<FftFrame>? PlaybackFftReady;

    /// <summary>Fired on any state change (recording start/stop, playback start/stop, file loaded).</summary>
    event EventHandler? StateChanged;
}
