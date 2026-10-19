using Microsoft.Win32;

namespace Eario;

internal static class HiddenAppStore
{
    private const string KeyPath = @"Software\Eario";
    private const string ValueName = "HiddenApps";

    public static HashSet<string> Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return new HashSet<string>((key?.GetValue(ValueName) as string[]) ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public static void Hide(string processName) { var hidden = Load(); hidden.Add(processName); Save(hidden); }
    public static void Show(string processName) { var hidden = Load(); hidden.Remove(processName); Save(hidden); }
    private static void Save(IEnumerable<string> hidden)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, hidden.OrderBy(name => name).ToArray(), RegistryValueKind.MultiString);
    }
}
