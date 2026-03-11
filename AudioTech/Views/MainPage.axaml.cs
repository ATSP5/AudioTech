using AudioTech.ViewModels;

using Avalonia.Controls;
using Avalonia.Input;

namespace AudioTech.Views;

public partial class MainPage : UserControl
{
    public MainPage()
    {
        InitializeComponent();

        // Wire slider seek events after XAML is initialized
        RecordProgressSlider.AddHandler(
            PointerPressedEvent,
            (_, _) => (DataContext as MainPageViewModel)?.RecordPlay.BeginSeek(),
            handledEventsToo: true);

        RecordProgressSlider.AddHandler(
            PointerReleasedEvent,
            (_, _) => (DataContext as MainPageViewModel)
                ?.RecordPlay.EndSeek(RecordProgressSlider.Value),
            handledEventsToo: true);
    }
}
