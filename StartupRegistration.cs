using Microsoft.Win32;

namespace Eario;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static void EnsureEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            var path = Environment.ProcessPath;
            if (path is not null) key?.SetValue("Eario", $"\"{path}\"");
        }
        catch { /* Startup registration must never prevent the tray app opening. */ }
    }
}
