using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace CodexLimitMonitor.App.Services;

internal sealed class TrayService : IDisposable
{
    private readonly Icon _icon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _showItem;
    private readonly Forms.ToolStripMenuItem _compactItem;
    private readonly Forms.ToolStripMenuItem _topmostItem;
    private readonly Forms.ToolStripMenuItem _clickThroughItem;
    private readonly Forms.ToolStripMenuItem _autostartItem;
    private bool _widgetVisible;

    public TrayService()
    {
        _icon = CreateIcon();
        _showItem = CreateMenuItem("Показать виджет", (_, _) => ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty));
        _compactItem = CreateMenuItem("Компактный режим", (_, _) => CompactModeRequested?.Invoke(this, EventArgs.Empty));
        _topmostItem = CreateMenuItem("Поверх окон", (_, _) => TopmostRequested?.Invoke(this, EventArgs.Empty));
        _clickThroughItem = CreateMenuItem("Пропускать клики", (_, _) => ClickThroughRequested?.Invoke(this, EventArgs.Empty));
        _autostartItem = CreateMenuItem("Запускать с Windows", (_, _) => AutostartRequested?.Invoke(this, EventArgs.Empty));

        _menu = new Forms.ContextMenuStrip
        {
            BackColor = Color.FromArgb(255, 28, 32, 40),
            ForeColor = Color.FromArgb(255, 239, 242, 247),
            ShowImageMargin = false,
            Padding = new Forms.Padding(4),
        };
        _menu.Items.AddRange([
            _showItem,
            CreateMenuItem("Обновить сейчас", (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty)),
            new Forms.ToolStripSeparator(),
            _compactItem,
            _topmostItem,
            _clickThroughItem,
            new Forms.ToolStripSeparator(),
            CreateMenuItem("Настройки…", (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)),
            _autostartItem,
            CreateMenuItem("Диагностика", (_, _) => DiagnosticsRequested?.Invoke(this, EventArgs.Empty)),
            new Forms.ToolStripSeparator(),
            CreateMenuItem("Выход", (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)),
        ]);
        _menu.Opening += (_, _) => SynchronizeMenu();

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Codex Limit Monitor",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? ToggleVisibilityRequested;

    public event EventHandler? RefreshRequested;

    public event EventHandler? CompactModeRequested;

    public event EventHandler? TopmostRequested;

    public event EventHandler? ClickThroughRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? AutostartRequested;

    public event EventHandler? DiagnosticsRequested;

    public event EventHandler? ExitRequested;

    public void UpdateState(AppSettings settings, bool widgetVisible)
    {
        _widgetVisible = widgetVisible;
        _compactItem.Checked = settings.IsCompact;
        _topmostItem.Checked = settings.IsTopmost;
        _clickThroughItem.Checked = settings.IsClickThrough;
        _autostartItem.Checked = settings.StartWithWindows;
        SynchronizeMenu();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }

    private void SynchronizeMenu()
    {
        _showItem.Text = _widgetVisible ? "Скрыть виджет" : "Показать виджет";
    }

    private static Forms.ToolStripMenuItem CreateMenuItem(string text, EventHandler clickHandler)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(255, 239, 242, 247),
            Padding = new Forms.Padding(8, 4, 8, 4),
        };
        item.Click += clickHandler;
        return item;
    }

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(255, 24, 28, 35));
            using var accent = new System.Drawing.Pen(Color.FromArgb(255, 114, 230, 161), 3.5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            graphics.FillEllipse(background, 2, 2, 28, 28);
            graphics.DrawArc(accent, 7, 7, 18, 18, 140, 260);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint iconHandle);
}
