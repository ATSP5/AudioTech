using AudioTech.Application.Filters;

namespace AudioTech.Application.Services;

public sealed record CaptureDevice(int Index, string Name);

/// <summary>One processed FFT frame from a single capture channel.</summary>
public sealed record FftFrame(
    int    ChannelIndex,
    string DeviceName,
    float[] MagnitudeDb,
    float[] PhaseRadians,
    int    SampleRate,
    int    FftSize);

public interface IAudioCaptureService : IDisposable
{
    IReadOnlyList<CaptureDevice> GetAvailableDevices();

    /// <summary>Start (or restart) one named channel on the given device.</summary>
    void StartChannel(int channelIndex, int deviceIndex, int sampleRate, int fftSize);

    void StopChannel(int channelIndex);
    void StopAll();

    /// <summary>
    /// Apply a noise filter to all active and future channels.
    /// Safe to call while channels are running.
    /// </summary>
    void ConfigureFilter(FilterType type, float strength);

    /// <summary>
    /// Set the input gain (in dB) for the given channel.
    /// Gain is applied to raw samples before any filter.
    /// Safe to call while capture is running or stopped.
    /// </summary>
    void SetChannelGain(int channelIndex, float gainDb);

    /// <summary>
    /// Route filtered audio from channel 0 to the default speaker output.
    /// Safe to call while capture is running or stopped.
    /// </summary>
    void SetPassthrough(bool enabled);

    bool IsAnyRunning { get; }
    IReadOnlyList<int> ActiveChannels { get; }

    /// <summary>Raised on a ThreadPool thread — marshal to UI thread as needed.</summary>
    event EventHandler<FftFrame>? FftFrameReady;
}
