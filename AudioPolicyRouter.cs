using System.Runtime.InteropServices;

namespace Eario;

internal static class AudioPolicyRouter
{
    private const string RuntimeClass = "Windows.Media.Internal.AudioPolicyConfig";
    private static readonly Guid FactoryId = new("AB3D4648-E242-459F-B02F-541C70306324");

    public static void SetOutput(uint processId, string endpointId)
    {
        var className = IntPtr.Zero;
        var deviceName = IntPtr.Zero;
        var factory = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(RuntimeClass, RuntimeClass.Length, out className));
            var iid = FactoryId;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, ref iid, out factory));

            var persistedId = $@"\\?\SWD#MMDEVAPI#{endpointId}#{{e6327cad-dcec-4949-ae8a-991e976a79d2}}";
            Marshal.ThrowExceptionForHR(WindowsCreateString(persistedId, persistedId.Length, out deviceName));

            // IUnknown (3) + IInspectable (3) + 19 policy methods = slot 25.
            var vtable = Marshal.ReadIntPtr(factory);
            var method = Marshal.ReadIntPtr(vtable, 25 * IntPtr.Size);
            var setEndpoint = Marshal.GetDelegateForFunctionPointer<SetEndpointDelegate>(method);
            foreach (var role in new[] { 0, 1, 2 })
                Marshal.ThrowExceptionForHR(setEndpoint(factory, processId, 0, role, deviceName));
        }
        finally
        {
            if (factory != IntPtr.Zero) Marshal.Release(factory);
            if (deviceName != IntPtr.Zero) WindowsDeleteString(deviceName);
            if (className != IntPtr.Zero) WindowsDeleteString(className);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetEndpointDelegate(IntPtr self, uint processId, int flow, int role, IntPtr deviceId);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source, int length, out IntPtr value);
    [DllImport("combase.dll", PreserveSig = true)] private static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll", PreserveSig = true)] private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
}
