using AudioTech.Application.Services;
using AudioTech.Domain.Enums;

using NAudio.Dsp;
using NAudio.Wave;

namespace AudioTech.Infrastructure.Services;

/// <summary>
/// Analyses microphone measurements from audio files or live devices.
///
/// File pipeline: load → mono-mix → Hann-windowed FFT → broadband RMS SPL →
///   pairwise cross-correlation (downsampled to 4 kHz) for time alignment.
/// Live pipeline: simultaneous WaveInEvent capture for 3 s → same analysis.
/// </summary>
public sealed class MeasurementAnalysisService : IMeasurementAnalysisService
{
    private const int   FftSize           = 8192;
    private const int   CorrelationRate   = 4000;  // Hz after downsampling
    private const int   CaptureDurationMs = 3000;
    private const int   LiveSampleRate    = 44100;

    // ── Public entry point ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<MicrophoneMeasurementResult>> AnalyzeAsync(
        IReadOnlyList<MicrophoneMeasurementRequest> requests,
        CancellationToken ct)
    {
        var results = new List<MicrophoneMeasurementResult>();

        var fileReqs = requests.Where(r => r.SourceType == MicrophoneSourceType.File).ToList();
        var liveReqs = requests.Where(r => r.SourceType == MicrophoneSourceType.LiveDevice).ToList();

        // ── File sources ──────────────────────────────────────────────────────
        if (fileReqs.Count > 0)
        {
            var loaded = new List<(float[] Samples, int SampleRate, string? Error)>();
            foreach (var req in fileReqs)
                loaded.Add(TryLoadFile(req.FilePath));

            // Cross-correlate each file against the first reference file.
            double[] offsets = ComputeTimeOffsets(
                loaded.Where(l => l.Error is null).Select(l => (l.Samples, l.SampleRate)).ToList());

            int offsetIdx = 0;
            for (int i = 0; i < fileReqs.Count; i++)
            {
                var (samples, sr, error) = loaded[i];
                if (error is not null)
                {
                    results.Add(new MicrophoneMeasurementResult(fileReqs[i].MicId,
                        false, 0, 0, 0, error));
                    continue;
                }

                double offsetMs = offsets[offsetIdx++] * 1000.0;
                float spl       = ComputeSpl(samples, sr, offsetMs);
                float[] mag     = ComputeFftMagnitude(samples);
                float dom       = FindDominantFreq(mag, sr);

                results.Add(new MicrophoneMeasurementResult(
                    fileReqs[i].MicId, true, spl, dom, offsetMs));
            }
        }

        // ── Live sources ──────────────────────────────────────────────────────
        if (liveReqs.Count > 0)
        {
            var captures = await CaptureDevicesAsync(
                liveReqs.Select(r => r.DeviceIndex).ToList(), ct);

            for (int i = 0; i < liveReqs.Count; i++)
            {
                var (samples, error) = captures[i];
                if (error is not null)
                {
                    results.Add(new MicrophoneMeasurementResult(liveReqs[i].MicId,
                        false, 0, 0, 0, error));
                    continue;
                }

                float spl   = ComputeSpl(samples!, LiveSampleRate, 0);
                float[] mag = ComputeFftMagnitude(samples!);
                float dom   = FindDominantFreq(mag, LiveSampleRate);

                results.Add(new MicrophoneMeasurementResult(
                    liveReqs[i].MicId, true, spl, dom, 0));
            }
        }

        return results;
    }

    // ── File loading ──────────────────────────────────────────────────────────

    private static (float[] Samples, int SampleRate, string? Error) TryLoadFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ([], 44100, "No file assigned.");

        try
        {
            using var reader = new AudioFileReader(path);
            int sr       = reader.WaveFormat.SampleRate;
            int channels = reader.WaveFormat.Channels;

            var rawBuffer = new float[sr * channels * 60]; // up to 60 s
            int totalRead = 0, read;
            while ((read = reader.Read(rawBuffer, totalRead, rawBuffer.Length - totalRead)) > 0)
            {
                totalRead += read;
                if (totalRead >= rawBuffer.Length) break;
            }

            // Mono-mix down.
            int monoLen = totalRead / channels;
            var mono    = new float[monoLen];
            for (int i = 0; i < monoLen; i++)
            {
                float sum = 0;
                for (int ch = 0; ch < channels; ch++)
                    sum += rawBuffer[i * channels + ch];
                mono[i] = sum / channels;
            }

            return (mono, sr, null);
        }
        catch (Exception ex)
        {
            return ([], 44100, ex.Message);
        }
    }

    // ── SPL ───────────────────────────────────────────────────────────────────

    private static float ComputeSpl(float[] samples, int sampleRate, double offsetMs)
    {
        int startSample = (int)(offsetMs / 1000.0 * sampleRate);
        startSample = Math.Clamp(startSample, 0, Math.Max(0, samples.Length - 1));

        if (samples.Length == 0) return -100f;

        double sumSq = 0;
        int count    = 0;
        for (int i = startSample; i < samples.Length; i++)
        {
            sumSq += samples[i] * (double)samples[i];
            count++;
        }

        if (count == 0) return -100f;
        double rms = Math.Sqrt(sumSq / count);
        return (float)(20.0 * Math.Log10(Math.Max(rms, 1e-12)));
    }

    // ── FFT magnitude ─────────────────────────────────────────────────────────

    private static float[] ComputeFftMagnitude(float[] samples)
    {
        int n   = FftSize;
        var buf = new Complex[n];

        for (int i = 0; i < n; i++)
        {
            float s = i < samples.Length ? samples[i] : 0f;
            float w = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)))); // Hann
            buf[i] = new Complex { X = s * w, Y = 0 };
        }

        FastFourierTransform.FFT(true, (int)Math.Log2(n), buf);

        var mag = new float[n / 2];
        for (int i = 0; i < n / 2; i++)
            mag[i] = (float)Math.Sqrt(buf[i].X * (double)buf[i].X + buf[i].Y * (double)buf[i].Y);

        return mag;
    }

    private static float FindDominantFreq(float[] mag, int sampleRate)
    {
        int peak = 0;
        for (int i = 1; i < mag.Length; i++)
            if (mag[i] > mag[peak]) peak = i;
        return (float)peak * sampleRate / (2 * mag.Length);
    }

    // ── Cross-correlation time alignment ──────────────────────────────────────

    private static double[] ComputeTimeOffsets(
        IReadOnlyList<(float[] Samples, int SampleRate)> data)
    {
        var offsets = new double[data.Count];
        if (data.Count <= 1) return offsets; // nothing to align

        var refDown = Downsample(data[0].Samples, data[0].SampleRate, CorrelationRate);

        for (int i = 1; i < data.Count; i++)
        {
            var othDown = Downsample(data[i].Samples, data[i].SampleRate, CorrelationRate);
            offsets[i]  = CrossCorrelationLag(refDown, othDown);
        }
        return offsets;
    }

    private static float[] Downsample(float[] src, int srcRate, int targetRate)
    {
        int factor = Math.Max(1, srcRate / targetRate);
        int n      = src.Length / factor;
        var out_   = new float[n];
        for (int i = 0; i < n; i++) out_[i] = src[i * factor];
        return out_;
    }

    /// Returns lag in seconds: positive means 'other' is delayed vs 'ref'.
    private static double CrossCorrelationLag(float[] ref_, float[] other)
    {
        int maxLag = Math.Min(CorrelationRate * 10, Math.Min(ref_.Length, other.Length) / 2);

        double maxCorr = double.MinValue;
        int bestLag    = 0;

        for (int lag = -maxLag; lag <= maxLag; lag++)
        {
            double corr  = 0;
            int    start = Math.Max(0, lag);
            int    end   = Math.Min(ref_.Length, other.Length + lag);
            for (int i = start; i < end; i++)
            {
                int j = i - lag;
                if (j >= 0 && j < other.Length)
                    corr += ref_[i] * (double)other[j];
            }
            if (corr > maxCorr) { maxCorr = corr; bestLag = lag; }
        }

        return (double)bestLag / CorrelationRate;
    }

    // ── Live device capture ───────────────────────────────────────────────────

    private static async Task<IReadOnlyList<(float[]? Samples, string? Error)>> CaptureDevicesAsync(
        IReadOnlyList<int> deviceIndices, CancellationToken ct)
    {
        // Capture all devices simultaneously.
        var tasks = deviceIndices.Select(idx => CaptureOneDeviceAsync(idx, ct));
        return await Task.WhenAll(tasks);
    }

    private static async Task<(float[]? Samples, string? Error)> CaptureOneDeviceAsync(
        int deviceIndex, CancellationToken ct)
    {
        if (deviceIndex < 0)
            return (null, "No device selected.");

        try
        {
            if (deviceIndex >= WaveInEvent.DeviceCount)
                return (null, $"Device {deviceIndex} not found.");
        }
        catch
        {
            return (null, "Unable to enumerate audio devices.");
        }

        var samples = new System.Collections.Concurrent.ConcurrentBag<float>();

        try
        {
            using var waveIn = new WaveInEvent
            {
                DeviceNumber     = deviceIndex,
                WaveFormat       = new WaveFormat(LiveSampleRate, 16, 1),
                BufferMilliseconds = 50
            };

            waveIn.DataAvailable += (_, e) =>
            {
                for (int i = 0; i < e.BytesRecorded - 1; i += 2)
                {
                    short s = BitConverter.ToInt16(e.Buffer, i);
                    samples.Add(s / 32768f);
                }
            };

            waveIn.StartRecording();
            await Task.Delay(CaptureDurationMs, ct);
            waveIn.StopRecording();

            return (samples.ToArray(), null);
        }
        catch (OperationCanceledException)
        {
            return (null, "Cancelled.");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
