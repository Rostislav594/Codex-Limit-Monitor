using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using CodexLimitMonitor.App.ViewModels;

namespace CodexLimitMonitor.App;

public partial class MainWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const long TransparentExtendedStyle = 0x00000020L;
    private bool _isClickThrough;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SourceInitialized += OnSourceInitialized;
    }

    public event EventHandler? DragCompleted;

    public void SetClickThrough(bool enabled)
    {
        _isClickThrough = enabled;
        ApplyClickThrough();
    }

    public void PrepareForShutdown() => _allowClose = true;

    public void ShowAndActivate()
    {
        Show();
        Activate();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        DataContextChanged -= OnDataContextChanged;
        SourceInitialized -= OnSourceInitialized;
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (e.ClickCount == 2 && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ToggleModeCommand.Execute(parameter: null);
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            DragCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainWindowViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is MainWindowViewModel newViewModel)
        {
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyWindowSize(newViewModel);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is MainWindowViewModel viewModel &&
            e.PropertyName is nameof(MainWindowViewModel.WindowWidth) or nameof(MainWindowViewModel.WindowHeight))
        {
            ApplyWindowSize(viewModel);
        }
    }

    private void ApplyWindowSize(MainWindowViewModel viewModel)
    {
        Width = viewModel.WindowWidth;
        Height = viewModel.WindowHeight;
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => ApplyClickThrough();

    private void ApplyClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var styles = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
        var updatedStyles = _isClickThrough
            ? styles | TransparentExtendedStyle
            : styles & ~TransparentExtendedStyle;
        SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(updatedStyles));
    }

    private static T? FindAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
            {
                return match;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);
}
