using System.Windows;
using System.Windows.Input;
using CodexLimitMonitor.App.Services;
using CodexLimitMonitor.App.ViewModels;

namespace CodexLimitMonitor.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _currentSettings;
    private readonly SettingsViewModel _viewModel;

    internal SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _currentSettings = settings;
        _viewModel = new SettingsViewModel(settings);
        DataContext = _viewModel;
    }

    internal AppSettings? ResultSettings { get; private set; }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultSettings = _viewModel.ApplyTo(_currentSettings);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
