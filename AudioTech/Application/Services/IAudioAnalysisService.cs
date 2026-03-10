namespace AudioTech.Application.Services;

public sealed record AudioProperties(TimeSpan Duration, int SampleRate, int Channels);

public interface IAudioAnalysisService
{
    Task<AudioProperties> AnalyseAsync(string filePath, CancellationToken cancellationToken = default);
}
