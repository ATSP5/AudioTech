using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioTech.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainPageViewModel       MainPage      { get; }
    public SettingsViewModel       Settings      { get; }
    public RoomAcousticsViewModel  RoomAcoustics { get; }

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private bool _isPaneOpen;

    public MainViewModel(
        MainPageViewModel      mainPage,
        SettingsViewModel      settings,
        RoomAcousticsViewModel roomAcoustics)
    {
        MainPage      = mainPage;
        Settings      = settings;
        RoomAcoustics = roomAcoustics;
        _currentPage  = mainPage;
    }

    [RelayCommand]
    private void NavigateToMain()
    {
        CurrentPage = MainPage;
        IsPaneOpen  = false;
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        CurrentPage = Settings;
        IsPaneOpen  = false;
    }

    [RelayCommand]
    private void NavigateToRoomAcoustics()
    {
        CurrentPage = RoomAcoustics;
        IsPaneOpen  = false;
    }

    [RelayCommand]
    private void TogglePane() => IsPaneOpen = !IsPaneOpen;
}
