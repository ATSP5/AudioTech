using AudioTech.Application.Abstractions;
using AudioTech.Domain.Repositories;

namespace AudioTech.Application.Queries.GetAudioAnalysis;

public sealed class GetAudioAnalysisQueryHandler(IAudioFileRepository repository)
    : IQueryHandler<GetAudioAnalysisQuery, AudioAnalysisResult?>
{
    public async Task<AudioAnalysisResult?> HandleAsync(GetAudioAnalysisQuery query, CancellationToken cancellationToken = default)
    {
        var audioFile = await repository.GetByIdAsync(query.AudioFileId, cancellationToken);

        if (audioFile is null) return null;

        return new AudioAnalysisResult(
            audioFile.Id,
            audioFile.FileName,
            audioFile.FilePath.Value,
            audioFile.Duration,
            audioFile.SampleRate,
            audioFile.Channels,
            audioFile.IsAnalysed,
            audioFile.LoadedAt);
    }
}
