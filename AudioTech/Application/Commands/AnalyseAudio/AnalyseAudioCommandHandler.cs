using AudioTech.Application.Abstractions;
using AudioTech.Application.Services;
using AudioTech.Domain.Repositories;

namespace AudioTech.Application.Commands.AnalyseAudio;

public sealed class AnalyseAudioCommandHandler(
    IAudioFileRepository repository,
    IAudioAnalysisService audioAnalysisService)
    : ICommandHandler<AnalyseAudioCommand>
{
    public async Task HandleAsync(AnalyseAudioCommand command, CancellationToken cancellationToken = default)
    {
        var audioFile = await repository.GetByIdAsync(command.AudioFileId, cancellationToken)
            ?? throw new InvalidOperationException($"Audio file '{command.AudioFileId}' not found.");

        var properties = await audioAnalysisService.AnalyseAsync(audioFile.FilePath.Value, cancellationToken);

        audioFile.SetAudioProperties(properties.Duration, properties.SampleRate, properties.Channels);

        await repository.UpdateAsync(audioFile, cancellationToken);
    }
}
