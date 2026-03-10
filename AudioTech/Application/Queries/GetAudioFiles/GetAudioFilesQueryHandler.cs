using AudioTech.Application.Abstractions;
using AudioTech.Domain.Repositories;

namespace AudioTech.Application.Queries.GetAudioFiles;

public sealed class GetAudioFilesQueryHandler(IAudioFileRepository repository)
    : IQueryHandler<GetAudioFilesQuery, IReadOnlyList<AudioFileListItem>>
{
    public async Task<IReadOnlyList<AudioFileListItem>> HandleAsync(GetAudioFilesQuery query, CancellationToken cancellationToken = default)
    {
        var audioFiles = await repository.GetAllAsync(cancellationToken);

        return audioFiles
            .Select(f => new AudioFileListItem(f.Id, f.FileName, f.FilePath.Value, f.IsAnalysed, f.LoadedAt))
            .ToList()
            .AsReadOnly();
    }
}
