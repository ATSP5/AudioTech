using AudioTech.Application.Services;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace AudioTech.Infrastructure.Services;

public sealed class AvaloniaDialogService : IDialogService
{
    private static TopLevel GetTopLevel()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)
            Avalonia.Application.Current!.ApplicationLifetime!;
        return TopLevel.GetTopLevel(lifetime.MainWindow!)!;
    }

    public async Task<string?> ShowSaveFilePickerAsync(
        string suggestedName,
        IReadOnlyList<(string Name, string[] Patterns)> fileTypes)
    {
        var result = await GetTopLevel().StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                SuggestedFileName = suggestedName,
                FileTypeChoices   = BuildTypes(fileTypes)
            });

        return result?.Path.LocalPath;
    }

    public async Task<string?> ShowOpenFilePickerAsync(
        IReadOnlyList<(string Name, string[] Patterns)> fileTypes)
    {
        var results = await GetTopLevel().StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple  = false,
                FileTypeFilter = BuildTypes(fileTypes)
            });

        return results.FirstOrDefault()?.Path.LocalPath;
    }

    private static IReadOnlyList<FilePickerFileType> BuildTypes(
        IReadOnlyList<(string Name, string[] Patterns)> fileTypes) =>
        fileTypes
            .Select(t => new FilePickerFileType(t.Name)
                { Patterns = t.Patterns.Select(p => $"*.{p}").ToList() })
            .ToList();
}
