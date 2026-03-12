using System.Collections.Concurrent;
using System.Threading.Channels;

using AudioTech.Application.Filters;
using AudioTech.Application.Services;
using AudioTech.Infrastructure.Filters;

using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace AudioTech.Infrastructure.Services;

/// <summary>
/// Manages multiple simultaneous WaveIn capture channels.
///
/// Pipeline per channel (two independent concurrent tasks):
///
///   WaveIn callback (10 ms buffers)
///     └─► PendingBlocks channel
///               │
///         ProcessingLoopAsync  ← gain · filter · per-channel speaker · recording
///               │
///         FftQueue channel (bounded/drop-oldest)
///               │
///         FftLoopAsync         ← FFT · spectral filter · FftFrameReady
///
/// Each channel that has IsPassthrough=true owns its own low-latency output
/// player (WasapiOut shared-mode preferred; WaveOutEvent fallback).
/// The global SetPassthrough toggle acts as a master on/off.
/// </summary>
public sealed class MultiChannelCaptureService : IAudioCaptureService
{
    // ── Passthrough output latency targets ───────────────────────────────────

    private const int WasapiLatencyMs  = 20;   // WASAPI shared event-sync
    private const int WaveOutLatencyMs = 40;   // WMME fallback
    private const int BufferDurationMs = 60;   // must exceed output latency

    // ── Per-channel state ────────────────────────────────────────────────────

    private sealed class ChannelState : IDisposable
    {
        public required int         DeviceIndex { get; init; }
        public required string      DeviceName  { get; init; }
        public required int         SampleRate  { get; init; }
        public required int         FftSize     { get; init; }
        public required WaveInEvent WaveIn      { get; init; }

        public volatile float        GainLinear    = 1f;
        public volatile INoiseFilter Filter        = new PassthroughFilter();
        public volatile bool         IsPassthrough = false;

        // Per-channel low-latency output (null when passthrough is off)
        private IWavePlayer?         _player;
        private BufferedWaveProvider? _buffer;
        private readonly object       _outputLock = new();

        // ── Audio pipeline channels ──────────────────────────────────────────

        public Channel<float[]> PendingBlocks { get; } =
            Channel.CreateUnbounded<float[]>(
                new UnboundedChannelOptions { SingleReader = true });

        public Channel<float[]> FftQueue { get; } =
            Channel.CreateBounded<float[]>(new BoundedChannelOptions(2)
            {
                FullMode     = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

        public CancellationTokenSource Cts { get; } = new();

        // ── Per-channel speaker output ───────────────────────────────────────

        /// <summary>
        /// Lazily opens the output player the first time audio needs to be sent.
        /// Tries WasapiOut (event-sync, lowest latency) and falls back to WaveOutEvent.
        /// </summary>
        public BufferedWaveProvider? EnsureOutput()
        {
            if (_buffer != null) return _buffer;
            lock (_outputLock)
            {
                if (_buffer != null) return _buffer;

                var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
                var pb     = new BufferedWaveProvider(format)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration          = TimeSpan.FromMilliseconds(BufferDurationMs)
                };

                IWavePlayer player;
                try
                {
                    // WASAPI shared event-sync — lowest achievable latency without exclusive/ASIO
                    var wasapi = new WasapiOut(
                        AudioClientShareMode.Shared,
                        useEventSync: true,
                        latency: WasapiLatencyMs);
                    wasapi.Init(pb);
                    player = wasapi;
                }
                catch
                {
                    // WMME fallback (e.g. no default audio device, driver issue)
                    var wo = new WaveOutEvent { DesiredLatency = WaveOutLatencyMs };
                    wo.Init(pb);
                    player = wo;
                }

                player.Play();
                _player = player;
                _buffer = pb;   // published last — acts as gate
                return pb;
            }
        }

        public void StopOutput()
        {
            lock (_outputLock)
            {
                _player?.Stop();
                _player?.Dispose();
                _player = null;
                _buffer = null;
            }
        }

        public void Dispose()
        {
            Cts.Cancel();
            PendingBlocks.Writer.TryComplete();
            FftQueue.Writer.TryComplete();
            WaveIn.Dispose();
            Cts.Dispose();
            StopOutput();
        }
    }

    // ── Fields ───────────────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<int, ChannelState> _channels      = new();
    private readonly ConcurrentDictionary<int, float>        _channelGains  = new();
    private readonly ConcurrentDictionary<int, bool>         _channelPassthroughs = new();

    private FilterType         _filterType        = FilterType.None;
    private float              _filterStrength    = 0f;
    private EqualizerSettings? _equalizerSettings;

    // Master passthrough switch — volatile: written from UI, read from audio tasks
    private volatile bool _passthroughEnabled;

    public event EventHandler<FftFrame>?           FftFrameReady;
    public event EventHandler<FilteredSamplesArgs>? FilteredSamplesReady;

    public bool IsAnyRunning  => !_channels.IsEmpty;
    public IReadOnlyList<int> ActiveChannels =>
        _channels.Keys.OrderBy(k => k).ToList().AsReadOnly();

    // ── Public API ───────────────────────────────────────────────────────────

    public IReadOnlyList<CaptureDevice> GetAvailableDevices()
    {
        var list = new List<CaptureDevice>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            list.Add(new CaptureDevice(i, caps.ProductName));
        }
        return list.AsReadOnly();
    }

    public void StartChannel(int channelIndex, int deviceIndex, int sampleRate, int fftSize)
    {
        StopChannel(channelIndex);

        var caps   = WaveInEvent.GetCapabilities(deviceIndex);
        var waveIn = new WaveInEvent
        {
            DeviceNumber       = deviceIndex,
            WaveFormat         = new WaveFormat(sampleRate, 16, 1),
            BufferMilliseconds = 10          // 10 ms base capture latency
        };

        var state = new ChannelState
        {
            DeviceIndex   = deviceIndex,
            DeviceName    = caps.ProductName,
            SampleRate    = sampleRate,
            FftSize       = fftSize,
            WaveIn        = waveIn,
            Filter        = FilterFactory.Create(_filterType, _filterStrength, sampleRate, _equalizerSettings, channelIndex),
            GainLinear    = _channelGains.TryGetValue(channelIndex, out var g) ? g : 1f,
            IsPassthrough = _channelPassthroughs.TryGetValue(channelIndex, out var pt) && pt
        };

        waveIn.DataAvailable += (_, e) => EnqueueSamples(state, e);

        if (_channels.TryAdd(channelIndex, state))
        {
            _ = Task.Run(() => ProcessingLoopAsync(channelIndex, state, state.Cts.Token));
            _ = Task.Run(() => FftLoopAsync(channelIndex, state, state.Cts.Token));
            waveIn.StartRecording();
        }
    }

    public void ConfigureFilter(FilterType type, float strength)
    {
        _filterType     = type;
        _filterStrength = Math.Clamp(strength, 0f, 1f);

        foreach (var (idx, state) in _channels)
        {
            var f = FilterFactory.Create(type, strength, state.SampleRate, _equalizerSettings, idx);
            f.Reset();
            state.Filter = f;
        }
    }

    public void SetEqualizerSettings(EqualizerSettings settings)
    {
        _equalizerSettings = settings;
        if (_filterType == FilterType.Equalizer)
            ConfigureFilter(FilterType.Equalizer, 0f);
    }

    public void SetChannelGain(int channelIndex, float gainDb)
    {
        float linear = gainDb <= 0f ? 1f : MathF.Pow(10f, gainDb / 20f);
        _channelGains[channelIndex] = linear;
        if (_channels.TryGetValue(channelIndex, out var state))
            state.GainLinear = linear;
    }

    /// <summary>Master on/off for all passthrough output.</summary>
    public void SetPassthrough(bool enabled)
    {
        _passthroughEnabled = enabled;

        if (!enabled)
        {
            // Stop all per-channel players immediately
            foreach (var (_, state) in _channels)
                state.StopOutput();
        }
        // When re-enabled, players are lazily recreated in SendToSpeaker
    }

    /// <summary>Per-channel passthrough toggle (set from Settings page).</summary>
    public void SetChannelPassthrough(int channelIndex, bool enabled)
    {
        _channelPassthroughs[channelIndex] = enabled;

        if (_channels.TryGetValue(channelIndex, out var state))
        {
            state.IsPassthrough = enabled;
            if (!enabled)
                state.StopOutput();
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

    // ── WaveIn callback ───────────────────────────────────────────────────────

    private static void EnqueueSamples(ChannelState state, WaveInEventArgs e)
    {
        const int bytesPerSample = 2;
        int sampleCount = e.BytesRecorded / bytesPerSample;

        var chunk = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short s = BitConverter.ToInt16(e.Buffer, i * bytesPerSample);
            chunk[i] = s / 32768f;
        }

        state.PendingBlocks.Writer.TryWrite(chunk);
    }

    // ── Audio processing loop ─────────────────────────────────────────────────

    private async Task ProcessingLoopAsync(int channelIndex, ChannelState state, CancellationToken ct)
    {
        var fftAccum    = new float[state.FftSize];
        int fftAccumPos = 0;

        try
        {
            await foreach (var chunk in state.PendingBlocks.Reader.ReadAllAsync(ct))
            {
                // 1. Gain
                float gain = state.GainLinear;
                if (gain != 1f)
                    for (int i = 0; i < chunk.Length; i++)
                        chunk[i] *= gain;

                // 2. Filter
                state.Filter.Apply(chunk);

                // 3. Recording callback
                FilteredSamplesReady?.Invoke(this,
                    new FilteredSamplesArgs(channelIndex, chunk, state.SampleRate));

                // 4. Speaker — immediately, decoupled from FFT
                if (_passthroughEnabled && state.IsPassthrough)
                    SendToSpeaker(state, chunk);

                // 5. Accumulate for FFT
                int srcPos = 0;
                while (srcPos < chunk.Length)
                {
                    int toCopy = Math.Min(chunk.Length - srcPos, state.FftSize - fftAccumPos);
                    Array.Copy(chunk, srcPos, fftAccum, fftAccumPos, toCopy);
                    srcPos      += toCopy;
                    fftAccumPos += toCopy;

                    if (fftAccumPos >= state.FftSize)
                    {
                        state.FftQueue.Writer.TryWrite((float[])fftAccum.Clone());
                        fftAccumPos = 0;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── FFT analysis loop ─────────────────────────────────────────────────────

    private async Task FftLoopAsync(int channelIndex, ChannelState state, CancellationToken ct)
    {
        try
        {
            await foreach (var block in state.FftQueue.Reader.ReadAllAsync(ct))
                ProcessFft(channelIndex, state, block);
        }
        catch (OperationCanceledException) { }
    }

    // ── Per-channel speaker output ────────────────────────────────────────────

    private static void SendToSpeaker(ChannelState state, float[] samples)
    {
        var pb = state.EnsureOutput();
        if (pb == null) return;

        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        pb.AddSamples(bytes, 0, bytes.Length);
    }

    // ── FFT computation ───────────────────────────────────────────────────────

    private void ProcessFft(int channelIndex, ChannelState state, float[] samples)
    {
        int fftSize   = state.FftSize;
        var fftBuffer = new Complex[fftSize];

        for (int i = 0; i < fftSize; i++)
        {
            double window  = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (fftSize - 1)));
            fftBuffer[i].X = (float)(samples[i] * window);
            fftBuffer[i].Y = 0;
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

        if (state.Filter is ISpectralFilter spectral)
            spectral.ApplySpectral(magnitudeDb, state.SampleRate, fftSize);

        FftFrameReady?.Invoke(this, new FftFrame(
            channelIndex, state.DeviceName,
            magnitudeDb, phaseRadians,
            state.SampleRate, fftSize));
    }
}
