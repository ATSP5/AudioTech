using AudioTech.Application.Services;
using AudioTech.Domain.Entities;
using AudioTech.Domain.Enums;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioTech.ViewModels;

/// <summary>
/// Wraps a <see cref="MicrophoneNode"/> with observable UI state for Measure Mode.
/// </summary>
public partial class MicrophoneConfigViewModel : ViewModelBase
{
    private readonly IDialogService _dialog;
    private readonly Action         _onChanged;

    public MicrophoneNode Node { get; }

    // ── Source type toggle ─────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLiveDevice))]
    private bool _useFile = true;

    public bool IsLiveDevice => !UseFile;

    // ── File source ───────────────────────────────────────────────────────────
    [ObservableProperty] private string _fileDisplayName = "(no file)";

    // ── Live device source ────────────────────────────────────────────────────
    public IReadOnlyList<string> DeviceNames { get; }

    [ObservableProperty] private int _selectedDeviceIndex = -1;

    // ── Analysis result ───────────────────────────────────────────────────────
    [ObservableProperty] private float? _measuredSpl;
    [ObservableProperty] private string _splDisplay = "—";
    [ObservableProperty] private string _timeOffsetDisplay = string.Empty;
    [ObservableProperty] private bool   _hasError;
    [ObservableProperty] private string _errorText = string.Empty;

    // ── Readonly display ──────────────────────────────────────────────────────
    public string Label => Node.Label;

    public MicrophoneSourceType EffectiveSourceType =>
        UseFile ? MicrophoneSourceType.File : MicrophoneSourceType.LiveDevice;

    // ── Constructor ───────────────────────────────────────────────────────────
    public MicrophoneConfigViewModel(
        MicrophoneNode node,
        IDialogService dialog,
        IReadOnlyList<string> deviceNames,
        Action onChanged)
    {
        Node        = node;
        _dialog     = dialog;
        DeviceNames = deviceNames;
        _onChanged  = onChanged;

        if (node.AssignedFilePath is not null)
            _fileDisplayName = System.IO.Path.GetFileName(node.AssignedFilePath);

        // Default to Live if we have devices, File otherwise.
        _useFile = DeviceNames.Count == 0;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SetFileSource() { UseFile = true; }

    [RelayCommand]
    private void SetLiveSource() { UseFile = false; }

    [RelayCommand]
    private async Task BrowseFileAsync()
    {
        var path = await _dialog.ShowOpenFilePickerAsync(
        [
            ("Audio Files", ["*.wav", "*.mp3", "*.flac", "*.ogg", "*.aiff", "*.m4a"])
        ]);

        if (path is not null)
        {
            Node.AssignFile(path);
            FileDisplayName = System.IO.Path.GetFileName(path);
            UseFile         = true;
            ClearResult();
            _onChanged();
        }
    }

    // ── Public result API ─────────────────────────────────────────────────────

    public void SetResult(MicrophoneMeasurementResult result)
    {
        HasError    = !result.Success;
        ErrorText   = result.ErrorMessage ?? string.Empty;
        MeasuredSpl = result.Success ? result.SplDb : null;

        SplDisplay = result.Success
            ? $"{result.SplDb:F1} dBFS   {result.DominantFreqHz:F0} Hz peak"
            : $"Error: {result.ErrorMessage}";

        TimeOffsetDisplay = result.TimeOffsetMs != 0
            ? $"Δt = {result.TimeOffsetMs:+#0.0;-#0.0;0} ms vs ref"
            : string.Empty;
    }

    public void ClearResult()
    {
        MeasuredSpl       = null;
        SplDisplay        = "—";
        TimeOffsetDisplay = string.Empty;
        HasError          = false;
        ErrorText         = string.Empty;
    }

    // ── Selected device index changed → clear result ──────────────────────────
    partial void OnSelectedDeviceIndexChanged(int value) => ClearResult();
    partial void OnUseFileChanged(bool value) => ClearResult();
}
