using AudioTech.Application.Abstractions;

namespace AudioTech.Application.Commands.AnalyseAudio;

public sealed record AnalyseAudioCommand(Guid AudioFileId) : ICommand;
