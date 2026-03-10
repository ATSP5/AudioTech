using System.Collections.ObjectModel;

using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AudioTech.ViewModels;

public partial class MicrophoneChannelConfig : ObservableObject
{
    public int    ChannelIndex { get; }
    public string Label        { get; }
    public Color  Color        { get; }

    public ObservableCollection<string> AvailableDevices { get; } = [];

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _selectedDeviceIndex;

    public MicrophoneChannelConfig(int channelIndex)
    {
        ChannelIndex = channelIndex;
        Label        = $"Channel {channelIndex + 1}";
        Color        = ChannelPalette.ForChannel(channelIndex);
    }
}
