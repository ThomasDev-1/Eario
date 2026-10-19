using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace Eario;

internal sealed class AudioService
{
    private const uint DeviceStateActive = 1;
    private const int StgmRead = 0;
    private const string RouteKey = @"Software\Microsoft\Multimedia\Audio\DefaultEndpoint";
    private readonly IMMDeviceEnumerator _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

    public IReadOnlyList<AudioDeviceModel> GetOutputDevices()
    {
        var list = new List<AudioDeviceModel>();
        Marshal.ThrowExceptionForHR(_enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out var devices));
        devices.GetCount(out var count);
        _enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var defaultDevice);
        defaultDevice.GetId(out var defaultId);
        for (uint i = 0; i < count; i++)
        {
            devices.Item(i, out var device);
            device.GetId(out var id);
            device.OpenPropertyStore(StgmRead, out var store);
            var key = PropertyKey.DeviceFriendlyName;
            store.GetValue(ref key, out var value);
            var name = value.GetString() ?? "Audio output";
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 23, IntPtr.Zero, out var volumeObject);
            var endpoint = (IAudioEndpointVolume)volumeObject;
            endpoint.GetMasterVolumeLevelScalar(out var volume);
            list.Add(new AudioDeviceModel { Id = id, Name = name, Volume = volume, IsDefault = id == defaultId });
            Marshal.ReleaseComObject(endpoint);
            Marshal.ReleaseComObject(store);
            Marshal.ReleaseComObject(device);
        }
        Marshal.ReleaseComObject(defaultDevice);
        Marshal.ReleaseComObject(devices);
        return list;
    }

    public void SetVolume(string deviceId, float volume)
    {
        _enumerator.GetDevice(deviceId, out var device);
        var iid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(ref iid, 23, IntPtr.Zero, out var obj);
        ((IAudioEndpointVolume)obj).SetMasterVolumeLevelScalar(Math.Clamp(volume, 0, 1), Guid.Empty);
        Marshal.ReleaseComObject(obj);
        Marshal.ReleaseComObject(device);
    }

    public void SetDefault(string deviceId)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications }) policy.SetDefaultEndpoint(deviceId, role);
        Marshal.ReleaseComObject(policy);
    }

    public IReadOnlyList<int> GetAudioSessionProcessIds(string deviceId)
    {
        var processIds = new HashSet<int>();
        _enumerator.GetDevice(deviceId, out var device);
        try
        {
            var iid = typeof(IAudioSessionManager2).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out var managerObject));
            var manager = (IAudioSessionManager2)managerObject;
            Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out var sessions));
            Marshal.ThrowExceptionForHR(sessions.GetCount(out var count));
            for (var i = 0; i < count; i++)
            {
                Marshal.ThrowExceptionForHR(sessions.GetSession(i, out var session));
                try
                {
                    if (session is IAudioSessionControl2 session2)
                    {
                        session2.GetProcessId(out var processId);
                        if (processId > 0) processIds.Add(unchecked((int)processId));
                    }
                }
                finally { Marshal.ReleaseComObject(session); }
            }
            Marshal.ReleaseComObject(sessions);
            Marshal.ReleaseComObject(manager);
        }
        finally { Marshal.ReleaseComObject(device); }
        return processIds.ToList();
    }

    public static void RouteApp(AppModel app, string deviceId)
    {
        var processIds = new HashSet<int> { app.ProcessId };
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(app.ExecutablePath)))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, app.ExecutablePath, StringComparison.OrdinalIgnoreCase)) processIds.Add(process.Id);
            }
            catch
            {
                // ignored
            }
            finally { process.Dispose(); }
        }

        Exception? policyError = null;
        var routed = false;
        foreach (var processId in processIds)
        {
            try { AudioPolicyRouter.SetOutput(unchecked((uint)processId), deviceId); routed = true; }
            catch (Exception ex) { policyError = ex; }
        }
        if (!routed) throw new InvalidOperationException("Windows rejected the application audio route.", policyError);

        using var root = Registry.CurrentUser.CreateSubKey(RouteKey);
        var subName = FindRouteKey(root, app.ExecutablePath) ?? CreateRouteName(app.ExecutablePath);
        using var route = root.CreateSubKey(subName, writable: true);
        route.SetValue(null, NormalizeNtPath(app.ExecutablePath));
        var endpointPath = $@"\\?\SWD#MMDEVAPI#{deviceId}#{{e6327cad-dcec-4949-ae8a-991e976a79d2}}";
        route.SetValue("000_000", endpointPath);
        route.SetValue("001_000", endpointPath);
    }

    public string? GetRoutedDevice(AppModel app, IEnumerable<AudioDeviceModel> devices)
    {
        using var root = Registry.CurrentUser.OpenSubKey(RouteKey);
        if (root is null) return null;
        var keyName = FindRouteKey(root, app.ExecutablePath);
        if (keyName is null) return null;
        using var key = root.OpenSubKey(keyName);
        var endpoint = key?.GetValue("000_000") as string;
        return devices.FirstOrDefault(d => endpoint?.Contains(d.Id, StringComparison.OrdinalIgnoreCase) == true)?.Id;
    }

    private static string? FindRouteKey(RegistryKey root, string path)
    {
        var ntPath = NormalizeNtPath(path);
        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (string.Equals(key?.GetValue(null) as string, ntPath, StringComparison.OrdinalIgnoreCase)) return name;
        }
        return null;
    }

    private static string CreateRouteName(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant()[..8] + "_0";
    private static string NormalizeNtPath(string path)
    {
        if (path.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)) return path;
        var root = Path.GetPathRoot(path)?.TrimEnd('\\');
        if (root is null) return path;
        var drive = QueryDosDevice(root.TrimEnd(':'), new StringBuilder(512), 512);
        var buffer = new StringBuilder(512);
        if (drive != 0 && QueryDosDevice(root.TrimEnd(':'), buffer, buffer.Capacity) != 0) return buffer + path[root.Length..];
        return path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint QueryDosDevice(string name, StringBuilder target, int max);
}

internal enum EDataFlow { Render, Capture, All }
internal enum ERole { Console, Multimedia, Communications }

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] internal class MMDeviceEnumerator { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, uint stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IMMDevice device); }
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
    [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out uint state);
}
[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    int GetCount(out uint count); int GetAt(uint index, out PropertyKey key); int GetValue(ref PropertyKey key, out PropVariant value); int SetValue(ref PropertyKey key, ref PropVariant value); int Commit();
}
[StructLayout(LayoutKind.Sequential)] internal struct PropertyKey
{
    public Guid FormatId; public uint PropertyId;
    public static PropertyKey DeviceFriendlyName => new() { FormatId = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14 };
}
[StructLayout(LayoutKind.Explicit)] internal struct PropVariant
{
    [FieldOffset(0)] private ushort type; [FieldOffset(8)] private IntPtr pointer;
    public readonly string? GetString() => type == 31 ? Marshal.PtrToStringUni(pointer) : null;
}
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    int RegisterControlChangeNotify(IntPtr notify); int UnregisterControlChangeNotify(IntPtr notify); int GetChannelCount(out uint count); int SetMasterVolumeLevel(float db, Guid context); int SetMasterVolumeLevelScalar(float level, Guid context); int GetMasterVolumeLevel(out float db); int GetMasterVolumeLevelScalar(out float level); int SetChannelVolumeLevel(uint channel, float db, Guid context); int SetChannelVolumeLevelScalar(uint channel, float level, Guid context); int GetChannelVolumeLevel(uint channel, out float db); int GetChannelVolumeLevelScalar(uint channel, out float level); int SetMute(bool mute, Guid context); int GetMute(out bool mute); int GetVolumeStepInfo(out uint step, out uint count); int VolumeStepUp(Guid context); int VolumeStepDown(Guid context); int QueryHardwareSupport(out uint mask); int GetVolumeRange(out float min, out float max, out float increment);
}
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IntPtr control);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out IntPtr volume);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
    int RegisterSessionNotification(IntPtr notification);
    int UnregisterSessionNotification(IntPtr notification);
    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr notification);
    int UnregisterDuckNotification(IntPtr notification);
}
[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
}
[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    int GetState(out int state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, Guid context);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, Guid context);
    int GetGroupingParam(out Guid grouping);
    int SetGroupingParam(Guid grouping, Guid context);
    int RegisterAudioSessionNotification(IntPtr client);
    int UnregisterAudioSessionNotification(IntPtr client);
}
[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    int GetState(out int state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, Guid context);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, Guid context);
    int GetGroupingParam(out Guid grouping);
    int SetGroupingParam(Guid grouping, Guid context);
    int RegisterAudioSessionNotification(IntPtr client);
    int UnregisterAudioSessionNotification(IntPtr client);
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetProcessId(out uint processId);
    int IsSystemSoundsSession();
    int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}
[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] internal class PolicyConfigClient { }
[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    int GetMixFormat(string id, IntPtr format); int GetDeviceFormat(string id, int current, IntPtr format); int ResetDeviceFormat(string id); int SetDeviceFormat(string id, IntPtr endpoint, IntPtr mix); int GetProcessingPeriod(string id, int current, IntPtr def, IntPtr min); int SetProcessingPeriod(string id, IntPtr period); int GetShareMode(string id, IntPtr mode); int SetShareMode(string id, IntPtr mode); int GetPropertyValue(string id, int store, IntPtr key, IntPtr value); int SetPropertyValue(string id, int store, IntPtr key, IntPtr value); int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role); int SetEndpointVisibility(string id, int visible);
}
