using AudioTech.Application.Abstractions;

namespace AudioTech.Application.Commands.LoadAudioFile;

public sealed record LoadAudioFileCommand(string FilePath) : ICommand<Guid>;
