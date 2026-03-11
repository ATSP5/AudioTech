namespace AudioTech.Application.Services;

public interface IDialogService
{
    Task<string?> ShowSaveFilePickerAsync(
        string suggestedName,
        IReadOnlyList<(string Name, string[] Patterns)> fileTypes);

    Task<string?> ShowOpenFilePickerAsync(
        IReadOnlyList<(string Name, string[] Patterns)> fileTypes);
}
