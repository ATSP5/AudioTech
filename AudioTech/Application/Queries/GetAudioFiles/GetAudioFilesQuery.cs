using AudioTech.Application.Abstractions;

namespace AudioTech.Application.Queries.GetAudioFiles;

public sealed record GetAudioFilesQuery : IQuery<IReadOnlyList<AudioFileListItem>>;
