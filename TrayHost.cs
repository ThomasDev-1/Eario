using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Eario;

internal sealed class TrayHost : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly MainWindow _window = new();
    private readonly AudioService _audio = new();
    private readonly Forms.ToolStripMenuItem _hiddenMenu = new("Hidden devices");
    private readonly Forms.ToolStripMenuItem _hiddenAppsMenu = new("Hidden apps");

    public TrayHost()
    {
        // Windows can silently skip adding a notification-area entry when Visible is
        // set before the icon and its menu are ready. Configure everything first and
        // register the completed icon with Explorer as the final step.
        _icon = new Forms.NotifyIcon { Text = "Eario", Icon = LoadAppIcon() };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Toggle(); };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Toggle());
        menu.Items.Add(_hiddenMenu);
        menu.Items.Add(_hiddenAppsMenu);
        menu.Opening += (_, _) => { PopulateHiddenDevices(); PopulateHiddenApps(); };
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());
        _icon.ContextMenuStrip = menu;
        _icon.Visible = true;
    }

    private void PopulateHiddenApps()
    {
        _hiddenAppsMenu.DropDownItems.Clear();
        var hidden = HiddenAppStore.Load().OrderBy(name => name).ToList();
        if (hidden.Count == 0)
        {
            _hiddenAppsMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("None") { Enabled = false });
            return;
        }
        foreach (var processName in hidden)
        {
            var item = new Forms.ToolStripMenuItem(processName) { Tag = processName };
            item.Click += (_, _) => _window.RestoreApp((string)item.Tag!);
            _hiddenAppsMenu.DropDownItems.Add(item);
        }
    }

    private void PopulateHiddenDevices()
    {
        _hiddenMenu.DropDownItems.Clear();
        var hidden = HiddenDeviceStore.Load();
        IReadOnlyList<AudioDeviceModel> devices;
        try { devices = _audio.GetOutputDevices().Where(device => hidden.Contains(device.Id)).ToList(); }
        catch { devices = []; }
        if (devices.Count == 0)
        {
            _hiddenMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("None") { Enabled = false });
            return;
        }
        foreach (var device in devices)
        {
            var item = new Forms.ToolStripMenuItem(device.Name) { Tag = device.Id };
            item.Click += (_, _) => _window.RestoreDevice((string)item.Tag!);
            _hiddenMenu.DropDownItems.Add(item);
        }
    }

    private void Toggle()
    {
        if (_window.IsVisible) _window.Hide(); else _window.OpenAtTray();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null) return (Icon)icon.Clone();
            }
        }
        catch { }
        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
