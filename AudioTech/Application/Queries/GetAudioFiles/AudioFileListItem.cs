namespace AudioTech.Application.Queries.GetAudioFiles;

public sealed record AudioFileListItem(
    Guid Id,
    string FileName,
    string FilePath,
    bool IsAnalysed,
    DateTime LoadedAt);
