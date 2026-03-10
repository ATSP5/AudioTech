using AudioTech.Application.Services;

namespace AudioTech.Infrastructure.Services;

public sealed class AudioAnalysisService : IAudioAnalysisService
{
    public async Task<AudioProperties> AnalyseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // TODO: Implement real audio analysis using a library (e.g. NAudio, FFMpegCore)
        await Task.Delay(100, cancellationToken); // placeholder for async I/O

        return new AudioProperties(
            Duration: TimeSpan.FromSeconds(30),
            SampleRate: 44100,
            Channels: 2);
    }
}
