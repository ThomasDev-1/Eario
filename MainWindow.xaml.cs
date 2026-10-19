using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace Eario;

public partial class MainWindow : Window
{
    private readonly AudioService _audio = new();
    private System.Windows.Point _dragStart;
    private AppModel? _draggedApp;
    private bool _loading;
    public ObservableCollection<AudioDeviceModel> Devices { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += ThemeChanged;
        Closed += (_, _) => SystemEvents.UserPreferenceChanged -= ThemeChanged;
    }

    public void OpenAtTray()
    {
        Reload();
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 12;
        Top = area.Bottom - Height - 12;
        Show();
        Activate();
    }

    private void Reload()
    {
        try
        {
            _loading = true;
            Devices.Clear();
            var hidden = HiddenDeviceStore.Load();
            var hiddenApps = HiddenAppStore.Load();
            foreach (var device in _audio.GetOutputDevices().Where(device => !hidden.Contains(device.Id))) Devices.Add(device);
            var apps = AppDiscovery.GetOpenApps().Where(app => !hiddenApps.Contains(app.Name)).ToList();
            var appsByPath = apps.ToDictionary(app => app.ExecutablePath, StringComparer.OrdinalIgnoreCase);

            var liveLocations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Collect live sessions, but choose one final card per executable below. Windows can
            // briefly retain the old session after a route changes.
            foreach (var device in Devices)
            {
                IReadOnlyList<int> processIds;
                try { processIds = _audio.GetAudioSessionProcessIds(device.Id); }
                catch { processIds = []; }
                foreach (var processId in processIds)
                {
                    var app = AppDiscovery.GetApp(processId);
                    if (app is null || hiddenApps.Contains(app.Name)) continue;
                    if (!appsByPath.TryGetValue(app.ExecutablePath, out var existing))
                    {
                        apps.Add(app);
                        appsByPath[app.ExecutablePath] = app;
                        existing = app;
                    }
                    liveLocations[existing.ExecutablePath] = device.Id;
                }
            }

            foreach (var app in apps)
            {
                var routed = _audio.GetRoutedDevice(app, Devices);
                liveLocations.TryGetValue(app.ExecutablePath, out var liveDevice);
                (Devices.FirstOrDefault(d => d.Id == routed) ?? Devices.FirstOrDefault(d => d.Id == liveDevice) ?? Devices.FirstOrDefault(d => d.IsDefault) ?? Devices.FirstOrDefault())?.Apps.Add(app);
            }
        }
        catch { }
        finally { _loading = false; }
    }
    public void RestoreDevice(string deviceId) { HiddenDeviceStore.Show(deviceId); Reload(); }
    private void HideDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem item || item.Parent is not System.Windows.Controls.ContextMenu menu || menu.PlacementTarget is not FrameworkElement target || target.DataContext is not AudioDeviceModel device) return;
        HiddenDeviceStore.Hide(device.Id);
        Reload();
    }
    public void RestoreApp(string processName) { HiddenAppStore.Show(processName); Reload(); }
    private void HideApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem item || item.Parent is not System.Windows.Controls.ContextMenu menu || menu.PlacementTarget is not FrameworkElement target || target.DataContext is not AppModel app) return;
        HiddenAppStore.Hide(app.Name);
        Reload();
    }

    private void App_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { _dragStart = e.GetPosition(this); _draggedApp = (sender as FrameworkElement)?.DataContext as AppModel; }
    private void App_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedApp is null) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
            System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, _draggedApp, System.Windows.DragDropEffects.Move);
    }
    private void Device_DragOver(object sender, System.Windows.DragEventArgs e) => e.Effects = e.Data.GetDataPresent(typeof(AppModel)) ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
    private void Device_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(typeof(AppModel)) is not AppModel app || (sender as FrameworkElement)?.DataContext is not AudioDeviceModel target) return;
        foreach (var device in Devices) device.Apps.Remove(app);
        target.Apps.Add(app);
        try { AudioService.RouteApp(app, target.Id); }
        catch { }
        Reload();
    }
    private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || (sender as FrameworkElement)?.DataContext is not AudioDeviceModel device) return;
        try { _audio.SetVolume(device.Id, (float)e.NewValue); } catch { }
    }
    private void Device_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || (sender as FrameworkElement)?.DataContext is not AudioDeviceModel device) return;
        try
        {
            _audio.SetDefault(device.Id);
            Reload();
        }
        catch { }
    }
    private void Window_Deactivated(object sender, EventArgs e) => Hide();
    private void ThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.Invoke(ApplyTheme);
    private void ApplyTheme()
    {
        var light = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) as int? ?? 1) != 0;
        Resources["PanelBrush"] = new SolidColorBrush(light ? MediaColor.FromArgb(246, 247, 247, 248) : MediaColor.FromArgb(246, 28, 28, 30));
        Resources["CardBrush"] = new SolidColorBrush(light ? Colors.White : MediaColor.FromRgb(44, 44, 47));
        Resources["TextBrush"] = new SolidColorBrush(light ? MediaColor.FromArgb(235, 0, 0, 0) : MediaColor.FromArgb(240, 255, 255, 255));
        Resources["MutedBrush"] = new SolidColorBrush(light ? MediaColor.FromArgb(138, 0, 0, 0) : MediaColor.FromArgb(160, 255, 255, 255));
        Resources["HairlineBrush"] = new SolidColorBrush(light ? MediaColor.FromArgb(22, 0, 0, 0) : MediaColor.FromArgb(35, 255, 255, 255));
    }
}
