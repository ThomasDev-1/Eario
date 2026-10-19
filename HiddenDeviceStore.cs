using Microsoft.Win32;

namespace Eario;

internal static class HiddenDeviceStore
{
    private const string KeyPath = @"Software\Eario";
    private const string ValueName = "HiddenDevices";

    public static HashSet<string> Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return new HashSet<string>((key?.GetValue(ValueName) as string[]) ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public static void Hide(string deviceId)
    {
        var hidden = Load();
        hidden.Add(deviceId);
        Save(hidden);
    }

    public static void Show(string deviceId)
    {
        var hidden = Load();
        hidden.Remove(deviceId);
        Save(hidden);
    }

    private static void Save(IEnumerable<string> hidden)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, hidden.ToArray(), RegistryValueKind.MultiString);
    }
}
