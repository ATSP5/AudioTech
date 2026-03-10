namespace AudioTech.Application.Queries.GetAudioAnalysis;

public sealed record AudioAnalysisResult(
    Guid AudioFileId,
    string FileName,
    string FilePath,
    TimeSpan? Duration,
    int? SampleRate,
    int? Channels,
    bool IsAnalysed,
    DateTime LoadedAt);
