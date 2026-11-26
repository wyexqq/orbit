using System.Diagnostics;
using Microsoft.Win32;
using Orbit.Settings;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Orbit.Ui;

internal sealed class TrayIcon : IDisposable
{
    private const string IdleText = "Orbit — смена формата файлов";

    private readonly AppSettings settings;
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Drawing.Icon icon;
    private readonly List<(Forms.ToolStripMenuItem Item, Func<bool> Read)> toggles = [];
    private bool syncingChecks;

    public TrayIcon(AppSettings settings)
    {
        this.settings = settings;
        icon = DrawIcon();
        notifyIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = IdleText,
            ContextMenuStrip = BuildMenu(),
            Visible = true,
        };
    }

    public event Action? SettingsChanged;

    public event Action? ExitRequested;

    public void ShowBusy(bool busy)
    {
        notifyIcon.Text = busy ? "Orbit — меняю формат…" : IdleText;
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        icon.Dispose();
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
        menu.Items.Add(new Forms.ToolStripMenuItem("Orbit — меняет формат файлов") { Enabled = false });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Toggle("Удержать файл — открыть кольцо", () => settings.HoldEnabled, value => settings.HoldEnabled = value));
        menu.Items.Add(Toggle("Встряхнуть при перетаскивании", () => settings.ShakeEnabled, value => settings.ShakeEnabled = value));
        menu.Items.Add(Toggle("Alt+Q на выделенных файлах", () => settings.HotkeyEnabled, value => settings.HotkeyEnabled = value));
        menu.Items.Add(Toggle("Оставлять оригинал рядом", () => settings.KeepOriginals, value => settings.KeepOriginals = value));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Toggle("Запускать вместе с Windows", () => AppSettings.StartsWithWindows, value => AppSettings.StartsWithWindows = value));
        menu.Items.Add(Toggle("Пауза", () => settings.Paused, value => settings.Paused = value));
        menu.Items.Add(new Forms.ToolStripMenuItem("Открыть журнал", null, (_, _) => OpenLog()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Выход", null, (_, _) => ExitRequested?.Invoke()));
        menu.Opening += (_, _) => SyncChecks();
        return menu;
    }

    private void SyncChecks()
    {
        syncingChecks = true;
        foreach (var (item, read) in toggles)
        {
            item.Checked = read();
        }
        syncingChecks = false;
    }

    private Forms.ToolStripMenuItem Toggle(string text, Func<bool> read, Action<bool> write)
    {
        var item = new Forms.ToolStripMenuItem(text) { Checked = read(), CheckOnClick = true };
        toggles.Add((item, read));
        item.CheckedChanged += (_, _) =>
        {
            if (syncingChecks)
            {
                return;
            }
            write(item.Checked);
            settings.Save();
            SettingsChanged?.Invoke();
        };
        return item;
    }

    private static void OpenLog()
    {
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orbit", "orbit.log");
        if (File.Exists(logPath))
        {
            Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
        }
    }

    private static Drawing.Icon DrawIcon()
    {
        var size = Forms.SystemInformation.SmallIconSize.Width;
        var color = TaskbarIsLight() ? Drawing.Color.FromArgb(26, 29, 35) : Drawing.Color.White;
        using var bitmap = new Drawing.Bitmap(size, size);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var stroke = Math.Max(1.5f, size / 9f);
            using var pen = new Drawing.Pen(color, stroke);
            var inset = stroke / 2 + size * 0.12f;
            graphics.DrawEllipse(pen, inset, inset, size - inset * 2, size - inset * 2);
            var planet = size * 0.3f;
            using var brush = new Drawing.SolidBrush(color);
            graphics.FillEllipse(brush, size - planet - size * 0.04f, size * 0.04f, planet, planet);
        }
        var iconHandle = bitmap.GetHicon();
        return Drawing.Icon.FromHandle(iconHandle);
    }

    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }
}
