using AudioTech.Application.Abstractions;
using AudioTech.Application.Commands.AnalyseAudio;
using AudioTech.Application.Commands.LoadAudioFile;
using AudioTech.Application.Queries.GetAudioAnalysis;
using AudioTech.Application.Queries.GetAudioFiles;
using AudioTech.Application.Services;
using AudioTech.Domain.Repositories;
using AudioTech.Infrastructure.Dispatchers;
using AudioTech.Infrastructure.Repositories;
using AudioTech.Infrastructure.Services;

using Microsoft.Extensions.DependencyInjection;

namespace AudioTech.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Dispatchers
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<IQueryDispatcher, QueryDispatcher>();

        // Repositories
        services.AddSingleton<IAudioFileRepository, InMemoryAudioFileRepository>();

        // Infrastructure services
        services.AddSingleton<IAudioAnalysisService, AudioAnalysisService>();
        services.AddSingleton<IAudioCaptureService, MultiChannelCaptureService>();
        services.AddSingleton<IAudioRecordPlayService, AudioRecordPlayService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();

        // Command handlers
        services.AddTransient<ICommandHandler<LoadAudioFileCommand, Guid>, LoadAudioFileCommandHandler>();
        services.AddTransient<ICommandHandler<AnalyseAudioCommand>, AnalyseAudioCommandHandler>();

        // Query handlers
        services.AddTransient<IQueryHandler<GetAudioAnalysisQuery, AudioAnalysisResult?>, GetAudioAnalysisQueryHandler>();
        services.AddTransient<IQueryHandler<GetAudioFilesQuery, IReadOnlyList<AudioFileListItem>>, GetAudioFilesQueryHandler>();

        return services;
    }
}
