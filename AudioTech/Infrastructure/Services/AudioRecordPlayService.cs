using AudioTech.Application.Filters;
using AudioTech.Application.Services;
using AudioTech.Infrastructure.Filters;

using Avalonia.Threading;

using NAudio.Dsp;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace AudioTech.Infrastructure.Services;

/// <summary>
/// Records filtered microphone audio and plays back audio files with
/// real-time FFT analysis. Thread-safe: recording callbacks arrive on
/// the capture ThreadPool; playback runs on its own background Task.
/// </summary>
public sealed class AudioRecordPlayService : IAudioRecordPlayService
{
    private const int PlaybackFftSize = 4096;

    // ── Recording ─────────────────────────────────────────────────────────────

    private readonly List<float> _recordBuffer = [];
    private int                  _recordSampleRate;
    private volatile bool        _isRecording;

    private readonly System.Diagnostics.Stopwatch _recordWatch = new();

    // ── Playback ──────────────────────────────────────────────────────────────

    private AudioFileReader?        _fileReader;
    private WaveOutEvent?           _waveOut;
    private BufferedWaveProvider?   _playbackBuffer;
    private CancellationTokenSource? _playCts;
    private volatile bool           _isPlaying;
    private volatile INoiseFilter   _playbackFilter = new PassthroughFilter();
    private int                     _playbackSampleRate;

    // Pre-computed file duration (available even when not playing)
    private TimeSpan _loadedFileDuration;

    // Temp WAV written after recording stops (enables playback without save)
    private string? _tempRecordingPath;
    private string? _loadedFilePath;

    // ── Events & state ────────────────────────────────────────────────────────

    public event EventHandler<FftFrame>? PlaybackFftReady;
    public event EventHandler?           StateChanged;

    public bool     IsRecording    => _isRecording;
    public bool     IsPlaying      => _isPlaying;
    public bool     HasLoadedFile  => _loadedFilePath != null && File.Exists(_loadedFilePath);
    public TimeSpan RecordingDuration => _recordWatch.Elapsed;
    public TimeSpan PlaybackPosition  => _fileReader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan PlaybackDuration  => _fileReader?.TotalTime ?? _loadedFileDuration;
    public string?  LoadedFilePath    => _loadedFilePath;

    // ── ctor / dispose ────────────────────────────────────────────────────────

    public AudioRecordPlayService(IAudioCaptureService captureService)
    {
        captureService.FilteredSamplesReady += OnFilteredSamples;
        MediaFoundationApi.Startup();
    }

    public void Dispose()
    {
        StopPlayback();

        if (_tempRecordingPath != null && File.Exists(_tempRecordingPath))
            File.Delete(_tempRecordingPath);

        MediaFoundationApi.Shutdown();
    }

    // ── Recording ─────────────────────────────────────────────────────────────

    public void StartRecording(int sampleRate)
    {
        if (_isRecording || _isPlaying) return;

        lock (_recordBuffer)
        {
            _recordBuffer.Clear();
            _recordSampleRate = sampleRate;
        }

        _recordWatch.Restart();
        _isRecording = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFilteredSamples(object? sender, FilteredSamplesArgs e)
    {
        if (!_isRecording || e.ChannelIndex != 0) return;

        lock (_recordBuffer)
        {
            if (_recordSampleRate == 0) _recordSampleRate = e.SampleRate;
            _recordBuffer.AddRange(e.Samples);
        }
    }

    public void StopRecording()
    {
        if (!_isRecording) return;

        _isRecording = false;
        _recordWatch.Stop();

        // Notify immediately so UI shows "recording stopped"
        StateChanged?.Invoke(this, EventArgs.Empty);

        // Persist to a temp WAV in the background, then fire StateChanged again
        // so the Play button becomes available.
        if (_recordBuffer.Count > 0)
            Task.Run(PersistTempRecordingAsync);
    }

    private async Task PersistTempRecordingAsync()
    {
        float[] samples;
        int sampleRate;

        lock (_recordBuffer)
        {
            samples    = _recordBuffer.ToArray();
            sampleRate = _recordSampleRate;
        }

        // Delete old temp file if present
        if (_tempRecordingPath != null && File.Exists(_tempRecordingPath))
            File.Delete(_tempRecordingPath);

        var path   = Path.Combine(Path.GetTempPath(), $"audiotech_{Guid.NewGuid():N}.wav");
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        await Task.Run(() =>
        {
            using var writer = new WaveFileWriter(path, format);
            writer.WriteSamples(samples, 0, samples.Length);
        });

        _tempRecordingPath  = path;
        _loadedFilePath     = path;
        _loadedFileDuration = GetFileDuration(path);

        // Second StateChanged: Play is now possible
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveRecordingAsync(string filePath, AudioSaveFormat format)
    {
        if (_tempRecordingPath == null || !File.Exists(_tempRecordingPath)) return;

        var src = _tempRecordingPath;

        await Task.Run(() =>
        {
            if (format == AudioSaveFormat.Wav)
            {
                File.Copy(src, filePath, overwrite: true);
            }
            else
            {
                using var reader = new AudioFileReader(src);
                MediaFoundationEncoder.EncodeToMp3(reader, filePath);
            }
        });
    }

    // ── File loading ──────────────────────────────────────────────────────────

    public void LoadFile(string filePath)
    {
        if (_isPlaying) StopPlayback();

        _loadedFilePath     = filePath;
        _loadedFileDuration = GetFileDuration(filePath);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static TimeSpan GetFileDuration(string path)
    {
        try   { using var r = new AudioFileReader(path); return r.TotalTime; }
        catch { return TimeSpan.Zero; }
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    public void StartPlayback(FilterType filterType, float filterStrength, EqualizerSettings? equalizerSettings = null)
    {
        if (_isPlaying || _isRecording) return;
        if (_loadedFilePath == null || !File.Exists(_loadedFilePath)) return;

        try { _fileReader = new AudioFileReader(_loadedFilePath); }
        catch { return; }

        int sampleRate = _fileReader.WaveFormat.SampleRate;
        _playbackSampleRate = sampleRate;

        // Create filter after file is open so we have the correct sample rate.
        // Pass equalizerSettings so the EQ filter uses the actual user settings.
        _playbackFilter = FilterFactory.Create(filterType, filterStrength, sampleRate, equalizerSettings);
        var format     = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        _playbackBuffer = new BufferedWaveProvider(format)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration          = TimeSpan.FromSeconds(2)
        };

        _waveOut = new WaveOutEvent();
        _waveOut.Init(_playbackBuffer);
        _waveOut.Play();

        _playCts  = new CancellationTokenSource();
        _isPlaying = true;

        StateChanged?.Invoke(this, EventArgs.Empty);

        _ = Task.Run(() => PlaybackLoopAsync(_playCts.Token));
    }

    public void StopPlayback()
    {
        if (!_isPlaying) return;

        _isPlaying = false;

        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = null;

        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;

        _playbackBuffer = null;

        _fileReader?.Dispose();
        _fileReader = null;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdatePlaybackFilter(FilterType filterType, float filterStrength, EqualizerSettings? equalizerSettings = null)
    {
        if (!_isPlaying) return;
        int sr = _playbackSampleRate > 0 ? _playbackSampleRate : 44100;
        var newFilter = FilterFactory.Create(filterType, filterStrength, sr, equalizerSettings);
        _playbackFilter = newFilter; // volatile write — visible to playback loop immediately
    }

    public void SeekTo(double fraction)
    {
        var reader = _fileReader;
        if (reader == null) return;

        fraction           = Math.Clamp(fraction, 0, 1);
        reader.CurrentTime = TimeSpan.FromSeconds(reader.TotalTime.TotalSeconds * fraction);
        _playbackBuffer?.ClearBuffer();
    }

    // ── Playback loop (runs on ThreadPool) ────────────────────────────────────

    private async Task PlaybackLoopAsync(CancellationToken ct)
    {
        var reader     = _fileReader!;
        int sampleRate = reader.WaveFormat.SampleRate;
        int channels   = reader.WaveFormat.Channels;

        // rawBuf: interleaved samples from AudioFileReader (may be stereo)
        var rawBuf   = new float[PlaybackFftSize * channels];
        var monoBuf  = new float[PlaybackFftSize];
        var outBytes = new byte[PlaybackFftSize * sizeof(float)];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Back-pressure: keep ~500 ms buffered — no more
                var buf = _playbackBuffer;
                if (buf != null && buf.BufferedDuration > TimeSpan.FromMilliseconds(500))
                {
                    await Task.Delay(20, ct).ConfigureAwait(false);
                    continue;
                }

                int read = reader.Read(rawBuf, 0, rawBuf.Length);
                if (read == 0) // EOF
                {
                    await Task.Delay(600, ct).ConfigureAwait(false); // let buffer drain
                    Dispatcher.UIThread.Post(StopPlayback);
                    return;
                }

                // Mix multi-channel down to mono
                int monoRead = read / channels;
                for (int i = 0; i < monoRead; i++)
                {
                    float sum = 0;
                    for (int c = 0; c < channels; c++)
                        sum += rawBuf[i * channels + c];
                    monoBuf[i] = sum / channels;
                }

                // Zero-pad for the filter / FFT if this was a short read
                if (monoRead < PlaybackFftSize)
                    Array.Clear(monoBuf, monoRead, PlaybackFftSize - monoRead);

                // Apply playback filter
                _playbackFilter.Apply(monoBuf);

                // Feed to speaker
                Buffer.BlockCopy(monoBuf, 0, outBytes, 0, monoRead * sizeof(float));
                buf?.AddSamples(outBytes, 0, monoRead * sizeof(float));

                // FFT → spectrum display
                PlaybackFftReady?.Invoke(this, ComputeFft(monoBuf, sampleRate, _playbackFilter));
            }
        }
        catch (OperationCanceledException) { /* normal on Stop */ }
    }

    // ── FFT (same Hanning + magnitude pipeline as MultiChannelCaptureService) ─

    private static FftFrame ComputeFft(float[] samples, int sampleRate, INoiseFilter filter)
    {
        int fftSize   = PlaybackFftSize;
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

        // Spectral filters (noise subtraction) operate on the magnitude spectrum
        if (filter is ISpectralFilter spectral)
            spectral.ApplySpectral(magnitudeDb, sampleRate, fftSize);

        return new FftFrame(0, "Playback", magnitudeDb, phaseRadians, sampleRate, fftSize);
    }
}
