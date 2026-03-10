using AudioTech.Application.Abstractions;

namespace AudioTech.Application.Queries.GetAudioAnalysis;

public sealed record GetAudioAnalysisQuery(Guid AudioFileId) : IQuery<AudioAnalysisResult?>;
