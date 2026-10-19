using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Eario;

internal static class AppDiscovery
{
    public static IReadOnlyList<AppModel> GetOpenApps()
    {
        var result = new List<AppModel>();
        var ownPid = Environment.ProcessId;
        foreach (var process in Process.GetProcesses().OrderBy(p => p.ProcessName))
        {
            try
            {
                if (process.Id == ownPid || process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle)) continue;
                var path = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(path)) continue;
                result.Add(new AppModel { ProcessId = process.Id, Name = process.ProcessName, ExecutablePath = path, Icon = GetIcon(path) });
            }
            catch
            {
                // ignored
            }
            finally { process.Dispose(); }
        }
        return result.GroupBy(x => x.ExecutablePath, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    }

    public static AppModel? GetApp(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var path = process.MainModule?.FileName;
            return string.IsNullOrWhiteSpace(path) ? null : new AppModel { ProcessId = process.Id, Name = process.ProcessName, ExecutablePath = path, Icon = GetIcon(path) };
        }
        catch { return null; }
    }

    private static ImageSource GetIcon(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is not null)
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(40, 40));
                image.Freeze();
                return image;
            }
        }
        catch
        {
            // ignored
        }

        var fallback = new DrawingImage(new DrawingGroup());
        fallback.Freeze();
        return fallback;
    }
}
