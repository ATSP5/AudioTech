using AudioTech.Application.Abstractions;
using AudioTech.Domain.Entities;
using AudioTech.Domain.Repositories;

namespace AudioTech.Application.Commands.LoadAudioFile;

public sealed class LoadAudioFileCommandHandler(IAudioFileRepository repository)
    : ICommandHandler<LoadAudioFileCommand, Guid>
{
    public async Task<Guid> HandleAsync(LoadAudioFileCommand command, CancellationToken cancellationToken = default)
    {
        var audioFile = AudioFile.Load(command.FilePath);

        await repository.AddAsync(audioFile, cancellationToken);

        return audioFile.Id;
    }
}
