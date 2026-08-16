using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using CodexLimitMonitor.App.Services;
using CodexLimitMonitor.App.ViewModels;
using CodexLimitMonitor.Codex.RateLimits;

namespace CodexLimitMonitor.App;

public partial class App : Application
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AutostartService _autostartService = new();
    private readonly FileDiagnosticLog _diagnosticLog = new();
    private readonly JsonSettingsService _settingsService = new();
    private SingleInstanceCoordinator? _singleInstance;
    private CodexRateLimitMonitor? _monitor;
    private MainWindowViewModel? _viewModel;
    private MainWindow? _window;
    private SettingsWindow? _settingsWindow;
    private DiagnosticsWindow? _diagnosticsWindow;
    private DiagnosticsViewModel? _diagnosticsViewModel;
    private TrayService? _trayService;
    private AppSettings _settings = new();
    private int _shutdownStarted;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var shutdownForUpdate = e.Args.Any(argument =>
            string.Equals(argument, "--shutdown-for-update", StringComparison.OrdinalIgnoreCase));
        _singleInstance = new SingleInstanceCoordinator();
        if (!_singleInstance.IsPrimaryInstance)
        {
            var primaryProcesses = shutdownForUpdate ? GetOtherAppProcesses() : [];
            await _singleInstance.SignalPrimaryAsync(
                shutdownForUpdate
                    ? SingleInstanceCommand.ShutdownForUpdate
                    : SingleInstanceCommand.Activate);
            var primaryExited = !shutdownForUpdate || await WaitForProcessesToExitAsync(
                primaryProcesses,
                TimeSpan.FromSeconds(15));
            await _singleInstance.DisposeAsync();
            _lifetime.Dispose();
            Shutdown(primaryExited ? 0 : 2);
            return;
        }

        if (shutdownForUpdate)
        {
            await _singleInstance.DisposeAsync();
            _lifetime.Dispose();
            Shutdown();
            return;
        }

        _singleInstance.ActivationRequested += OnActivationRequested;
        _singleInstance.ShutdownRequested += OnShutdownRequested;
        _singleInstance.StartListening();
        SubscribeToSystemEvents();

        _diagnosticLog.Write(
            CodexLimitMonitor.Core.Models.DiagnosticSeverity.Information,
            "Application",
            "application.start",
            "Приложение запущено.");

        _settings = await _settingsService.LoadAsync(_lifetime.Token);
        try
        {
            var autostart = _autostartService.Synchronize();
            _settings = _settings with { StartWithWindows = autostart.IsEnabled };
            if (autostart.WasMigrated)
            {
                _diagnosticLog.Write(
                    CodexLimitMonitor.Core.Models.DiagnosticSeverity.Information,
                    "Application",
                    "autostart.migrated",
                    "Путь автозапуска обновлён для текущей версии приложения.");
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            _diagnosticLog.Write(
                CodexLimitMonitor.Core.Models.DiagnosticSeverity.Warning,
                "Application",
                "autostart.read_failed",
                "Не удалось прочитать состояние автозапуска.");
        }

        _monitor = new CodexRateLimitMonitor(
            TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds),
            diagnosticLog: _diagnosticLog);
        _viewModel = new MainWindowViewModel(_monitor, _settings);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _window = new MainWindow
        {
            DataContext = _viewModel,
            Opacity = _settings.Opacity,
            Topmost = _settings.IsTopmost,
        };
        MainWindow = _window;
        RestoreWindowPosition(_window);
        _window.SetClickThrough(_settings.IsClickThrough);
        _window.DragCompleted += OnWindowDragCompleted;
        _window.IsVisibleChanged += OnWindowVisibilityChanged;

        _trayService = new TrayService();
        SubscribeToTray(_trayService);

        var launchedByAutostart = e.Args.Any(argument =>
            string.Equals(argument, "--autostart", StringComparison.OrdinalIgnoreCase));
        if (!_settings.StartMinimized && !launchedByAutostart)
        {
            _window.Show();
        }

        SynchronizeTrayState();

        try
        {
            await _viewModel.StartAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private void SubscribeToTray(TrayService tray)
    {
        tray.ToggleVisibilityRequested += (_, _) => ToggleWidgetVisibility();
        tray.RefreshRequested += (_, _) => _viewModel?.RefreshCommand.Execute(parameter: null);
        tray.CompactModeRequested += (_, _) => ToggleCompactMode();
        tray.TopmostRequested += (_, _) => ToggleTopmost();
        tray.ClickThroughRequested += (_, _) => ToggleClickThrough();
        tray.SettingsRequested += (_, _) => Dispatcher.BeginInvoke(ShowSettings);
        tray.AutostartRequested += (_, _) => ToggleAutostart();
        tray.DiagnosticsRequested += (_, _) => Dispatcher.BeginInvoke(ShowDiagnostics);
        tray.ExitRequested += async (_, _) => await ShutdownAsync();
    }

    private void OnActivationRequested(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(ShowWidget);

    private void OnShutdownRequested(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(async () => await ShutdownAsync());

    private static Process[] GetOtherAppProcesses()
    {
        using var currentProcess = Process.GetCurrentProcess();
        return Process.GetProcessesByName(currentProcess.ProcessName)
            .Where(process =>
                process.Id != currentProcess.Id &&
                process.SessionId == currentProcess.SessionId)
            .ToArray();
    }

    private static async Task<bool> WaitForProcessesToExitAsync(
        IEnumerable<Process> processes,
        TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            foreach (var process in processes)
            {
                using (process)
                {
                    await process.WaitForExitAsync(timeoutSource.Token);
                }
            }

            return true;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            return false;
        }
    }

    private void ToggleWidgetVisibility()
    {
        if (_window is null)
        {
            return;
        }

        if (_window.IsVisible)
        {
            _window.Hide();
        }
        else
        {
            ShowWidget();
        }
    }

    private void ShowWidget()
    {
        _window?.ShowAndActivate();
        SynchronizeTrayState();
    }

    private void ToggleCompactMode()
    {
        if (_viewModel is not null)
        {
            _viewModel.IsCompact = !_viewModel.IsCompact;
        }
    }

    private void ToggleTopmost()
    {
        _settings = _settings with { IsTopmost = !_settings.IsTopmost };
        if (_window is not null)
        {
            _window.Topmost = _settings.IsTopmost;
        }

        SettingsChanged();
    }

    private void ToggleClickThrough()
    {
        _settings = _settings with { IsClickThrough = !_settings.IsClickThrough };
        _window?.SetClickThrough(_settings.IsClickThrough);
        SettingsChanged();
    }

    private void ToggleAutostart()
    {
        var enabled = !_settings.StartWithWindows;
        try
        {
            _autostartService.SetEnabled(enabled);
            _settings = _settings with { StartWithWindows = enabled };
            SettingsChanged();
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            MessageBox.Show(
                "Не удалось изменить автозапуск для текущего пользователя.",
                "Codex Limit Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings);
        if (_window?.IsVisible == true)
        {
            _settingsWindow.Owner = _window;
        }

        try
        {
            if (_settingsWindow.ShowDialog() == true && _settingsWindow.ResultSettings is { } updated)
            {
                ApplySettings(updated);
            }
        }
        finally
        {
            _settingsWindow = null;
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        var previousAutostart = _settings.StartWithWindows;
        _settings = settings.Normalize();

        if (_window is not null)
        {
            _window.Opacity = _settings.Opacity;
            _window.Topmost = _settings.IsTopmost;
            _window.SetClickThrough(_settings.IsClickThrough);
        }

        if (_viewModel is not null)
        {
            _viewModel.IsCompact = _settings.IsCompact;
            _viewModel.ShowResetCountdown = _settings.ShowResetCountdown;
        }

        if (_monitor is not null)
        {
            _monitor.RefreshInterval = TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds);
        }

        if (previousAutostart != _settings.StartWithWindows)
        {
            try
            {
                _autostartService.SetEnabled(_settings.StartWithWindows);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                _settings = _settings with { StartWithWindows = previousAutostart };
            }
        }

        SettingsChanged();
    }

    private void ShowDiagnostics()
    {
        if (_diagnosticsWindow is not null)
        {
            _diagnosticsWindow.Activate();
            return;
        }

        if (_monitor is null)
        {
            return;
        }

        _diagnosticsViewModel = new DiagnosticsViewModel(_monitor, _diagnosticLog);
        _diagnosticsWindow = new DiagnosticsWindow
        {
            DataContext = _diagnosticsViewModel,
        };
        if (_window?.IsVisible == true)
        {
            _diagnosticsWindow.Owner = _window;
        }

        _diagnosticsWindow.Closed += OnDiagnosticsWindowClosed;
        _diagnosticsWindow.Show();
    }

    private void OnDiagnosticsWindowClosed(object? sender, EventArgs e)
    {
        if (_diagnosticsWindow is not null)
        {
            _diagnosticsWindow.Closed -= OnDiagnosticsWindowClosed;
        }

        _diagnosticsViewModel?.Dispose();
        _diagnosticsViewModel = null;
        _diagnosticsWindow = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsCompact) && _viewModel is not null)
        {
            _settings = _settings with { IsCompact = _viewModel.IsCompact };
            SettingsChanged();
        }
    }

    private void OnWindowDragCompleted(object? sender, EventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        _settings = _settings with
        {
            WindowLeft = _window.Left,
            WindowTop = _window.Top,
        };
        SettingsChanged();
    }

    private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        SynchronizeTrayState();

    private void SettingsChanged()
    {
        SynchronizeTrayState();
        SaveSettings();
    }

    private async void SaveSettings()
    {
        try
        {
            await _settingsService.SaveAsync(_settings, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void SynchronizeTrayState() =>
        _trayService?.UpdateState(_settings, _window?.IsVisible == true);

    private void RestoreWindowPosition(MainWindow window)
    {
        var position = WindowPlacementService.EnsureVisible(
            _settings.WindowLeft,
            _settings.WindowTop,
            _viewModel?.WindowWidth ?? window.Width,
            _viewModel?.WindowHeight ?? window.Height,
            GetWorkingAreas(window));
        window.Left = position.X;
        window.Top = position.Y;
    }

    private void EnsureWindowIsVisible()
    {
        if (_window is null)
        {
            return;
        }

        var position = WindowPlacementService.EnsureVisible(
            _window.Left,
            _window.Top,
            _window.Width,
            _window.Height,
            GetWorkingAreas(_window));
        if (Math.Abs(position.X - _window.Left) < 0.5 && Math.Abs(position.Y - _window.Top) < 0.5)
        {
            return;
        }

        _window.Left = position.X;
        _window.Top = position.Y;
        _settings = _settings with { WindowLeft = position.X, WindowTop = position.Y };
        SettingsChanged();
    }

    private static IReadOnlyList<Rect> GetWorkingAreas(Window window)
    {
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        var scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
        return System.Windows.Forms.Screen.AllScreens
            .OrderByDescending(screen => screen.Primary)
            .Select(screen => new Rect(
                screen.WorkingArea.Left / scaleX,
                screen.WorkingArea.Top / scaleY,
                screen.WorkingArea.Width / scaleX,
                screen.WorkingArea.Height / scaleY))
            .ToArray();
    }

    private void SubscribeToSystemEvents()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void UnsubscribeFromSystemEvents()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            _diagnosticLog.Write(
                CodexLimitMonitor.Core.Models.DiagnosticSeverity.Information,
                "System",
                "power.suspend",
                "Система переходит в спящий режим.");
            return;
        }

        if (e.Mode == PowerModes.Resume)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _diagnosticLog.Write(
                    CodexLimitMonitor.Core.Models.DiagnosticSeverity.Information,
                    "System",
                    "power.resume",
                    "Система вышла из спящего режима; запрошена ресинхронизация.");
                EnsureWindowIsVisible();
                _monitor?.RequestReconnect();
            });
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            _diagnosticLog.Write(
                CodexLimitMonitor.Core.Models.DiagnosticSeverity.Information,
                "System",
                "display.changed",
                "Конфигурация дисплеев изменилась.");
            EnsureWindowIsVisible();
        });

    private async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        if (_window is not null)
        {
            _settings = _settings with
            {
                WindowLeft = _window.Left,
                WindowTop = _window.Top,
            };
        }

        try
        {
            await _settingsService.SaveAsync(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        _lifetime.Cancel();
        UnsubscribeFromSystemEvents();
        _trayService?.Dispose();
        _settingsWindow?.Close();
        _diagnosticsWindow?.Close();

        if (_window is not null)
        {
            _window.DragCompleted -= OnWindowDragCompleted;
            _window.IsVisibleChanged -= OnWindowVisibilityChanged;
            _window.PrepareForShutdown();
            _window.Close();
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
        }

        if (_monitor is not null)
        {
            await _monitor.DisposeAsync();
        }

        if (_singleInstance is not null)
        {
            _singleInstance.ActivationRequested -= OnActivationRequested;
            _singleInstance.ShutdownRequested -= OnShutdownRequested;
            await _singleInstance.DisposeAsync();
        }

        _diagnosticLog.Write(
            CodexLimitMonitor.Core.Models.DiagnosticSeverity.Information,
            "Application",
            "application.stop",
            "Приложение остановлено.");
        _lifetime.Dispose();
        Shutdown();
    }
}
