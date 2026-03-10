using System.Collections.Concurrent;

using AudioTech.Application.Filters;
using AudioTech.Application.Services;
using AudioTech.Infrastructure.Filters;
// ISpectralFilter used for frequency-domain post-processing

using NAudio.Dsp;
using NAudio.Wave;

namespace AudioTech.Infrastructure.Services;

/// <summary>
/// Manages multiple simultaneous WaveIn capture channels.
/// Each channel buffers samples, applies the configured noise filter, then
/// processes FFT — all on a dedicated ThreadPool task per channel.
/// </summary>
public sealed class MultiChannelCaptureService : IAudioCaptureService
{
    // ── Per-channel state ────────────────────────────────────────────────────

    private sealed class ChannelState : IDisposable
    {
        public required int         DeviceIndex  { get; init; }
        public required string      DeviceName   { get; init; }
        public required int         SampleRate   { get; init; }
        public required int         FftSize      { get; init; }
        public required WaveInEvent WaveIn       { get; init; }

        public float[]  SampleBuffer { get; set; } = [];
        public int      BufferPos    { get; set; }

        // Swappable filter — replaced atomically when ConfigureFilter is called
        public volatile INoiseFilter Filter = new PassthroughFilter();

        public ConcurrentQueue<float[]> PendingBlocks { get; } = new();
        public CancellationTokenSource  Cts           { get; } = new();

        public void Dispose()
        {
            Cts.Cancel();
            WaveIn.Dispose();
            Cts.Dispose();
        }
    }

    // ── Fields ───────────────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<int, ChannelState> _channels = new();

    // Current filter config — applied when new channels start and when changed live
    private FilterType _filterType     = FilterType.None;
    private float      _filterStrength = 0f;

    public event EventHandler<FftFrame>? FftFrameReady;
    public bool IsAnyRunning     => !_channels.IsEmpty;
    public IReadOnlyList<int> ActiveChannels =>
        _channels.Keys.OrderBy(k => k).ToList().AsReadOnly();

    // ── Public API ───────────────────────────────────────────────────────────

    public IReadOnlyList<CaptureDevice> GetAvailableDevices()
    {
        var devices = new List<CaptureDevice>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            devices.Add(new CaptureDevice(i, caps.ProductName));
        }
        return devices.AsReadOnly();
    }

    public void StartChannel(int channelIndex, int deviceIndex, int sampleRate, int fftSize)
    {
        StopChannel(channelIndex);

        var caps   = WaveInEvent.GetCapabilities(deviceIndex);
        var waveIn = new WaveInEvent
        {
            DeviceNumber       = deviceIndex,
            WaveFormat         = new WaveFormat(sampleRate, 16, 1),
            BufferMilliseconds = 20
        };

        var state = new ChannelState
        {
            DeviceIndex  = deviceIndex,
            DeviceName   = caps.ProductName,
            SampleRate   = sampleRate,
            FftSize      = fftSize,
            WaveIn       = waveIn,
            SampleBuffer = new float[fftSize],
            Filter       = FilterFactory.Create(_filterType, _filterStrength)
        };

        waveIn.DataAvailable += (_, e) => EnqueueSamples(state, e);

        if (_channels.TryAdd(channelIndex, state))
        {
            _ = Task.Run(() => ProcessingLoopAsync(channelIndex, state, state.Cts.Token));
            waveIn.StartRecording();
        }
    }

    /// <summary>
    /// Applies a new filter to all active channels immediately and to any
    /// channels started afterwards. Thread-safe.
    /// </summary>
    public void ConfigureFilter(FilterType type, float strength)
    {
        _filterType     = type;
        _filterStrength = Math.Clamp(strength, 0f, 1f);

        foreach (var state in _channels.Values)
        {
            var newFilter = FilterFactory.Create(type, strength);
            newFilter.Reset();
            state.Filter = newFilter;   // volatile write — visible to processing thread
        }
    }

    public void StopChannel(int channelIndex)
    {
        if (_channels.TryRemove(channelIndex, out var state))
            state.Dispose();
    }

    public void StopAll()
    {
        foreach (var key in _channels.Keys.ToList())
            StopChannel(key);
    }

    public void Dispose() => StopAll();

    // ── Audio capture thread ─────────────────────────────────────────────────

    private static void EnqueueSamples(ChannelState state, WaveInEventArgs e)
    {
        const int bytesPerSample = 2;
        int sampleCount = e.BytesRecorded / bytesPerSample;

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(e.Buffer, i * bytesPerSample);
            state.SampleBuffer[state.BufferPos++] = sample / 32768f;

            if (state.BufferPos >= state.FftSize)
            {
                state.PendingBlocks.Enqueue((float[])state.SampleBuffer.Clone());
                state.BufferPos = 0;
            }
        }
    }

    // ── FFT processing thread (ThreadPool via Task.Run) ──────────────────────

    private async Task ProcessingLoopAsync(int channelIndex, ChannelState state, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (state.PendingBlocks.TryDequeue(out var samples))
            {
                // Apply filter in-place before FFT (volatile read of Filter)
                state.Filter.Apply(samples);
                ProcessFft(channelIndex, state, samples);
            }
            else
            {
                await Task.Delay(5, ct).ConfigureAwait(false);
            }
        }
    }

    private void ProcessFft(int channelIndex, ChannelState state, float[] samples)
    {
        int fftSize   = state.FftSize;
        var fftBuffer = new Complex[fftSize];

        for (int i = 0; i < fftSize; i++)
        {
            double window   = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (fftSize - 1))); // Hanning
            fftBuffer[i].X  = (float)(samples[i] * window);
            fftBuffer[i].Y  = 0;
        }

        FastFourierTransform.FFT(true, (int)Math.Log2(fftSize), fftBuffer);

        int bins         = fftSize / 2;
        var magnitudeDb  = new float[bins];
        var phaseRadians = new float[bins];

        for (int i = 0; i < bins; i++)
        {
            float re  = fftBuffer[i].X;
            float im  = fftBuffer[i].Y;
            float mag = MathF.Sqrt(re * re + im * im);
            magnitudeDb[i]  = mag > 0 ? 20f * MathF.Log10(mag) : -160f;
            phaseRadians[i] = MathF.Atan2(im, re);
        }

        // Spectral filters (white/purple noise subtraction) run after magnitude computation
        if (state.Filter is ISpectralFilter spectral)
            spectral.ApplySpectral(magnitudeDb, state.SampleRate, fftSize);

        FftFrameReady?.Invoke(this, new FftFrame(
            channelIndex, state.DeviceName,
            magnitudeDb, phaseRadians,
            state.SampleRate, fftSize));
    }
}
