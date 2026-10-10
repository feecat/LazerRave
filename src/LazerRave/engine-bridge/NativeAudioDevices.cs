using System.Runtime.InteropServices;
using System.Text;

namespace LazerRave.Bridge;

internal static class NativeAudioDevices
{
    public static IReadOnlyDictionary<int, string> Enumerate(string runtime, int output)
    {
        var devices = new Dictionary<int, string>();
        var library = NativeLibrary.Load(Path.Combine(runtime, "fmod.dll"));
        IntPtr system = IntPtr.Zero;
        try
        {
            T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            var create = Function<CreateSystem>("FMOD_System_Create");
            if (create(out system, 0x00020310) != 0 || system == IntPtr.Zero) return devices;
            if (Function<SetOutput>("FMOD_System_SetOutput")(system, output == 2 ? 7 : 6) != 0) return devices;
            if (Function<GetDriverCount>("FMOD_System_GetNumDrivers")(system, out int count) != 0) return devices;
            var info = Function<GetDriverInfo>("FMOD_System_GetDriverInfo");
            for (int index = 0; index < Math.Min(count, 256); index++)
            {
                var name = new byte[1024];
                if (info(system, index, name, name.Length, IntPtr.Zero, out _, out _, out _) != 0) continue;
                int end = Array.IndexOf(name, (byte)0);
                devices[index] = Encoding.UTF8.GetString(name, 0, end >= 0 ? end : name.Length);
            }
        }
        finally
        {
            try
            {
                if (system != IntPtr.Zero) Marshal.GetDelegateForFunctionPointer<ReleaseSystem>(NativeLibrary.GetExport(library, "FMOD_System_Release"))(system);
            }
            finally { NativeLibrary.Free(library); }
        }
        return devices;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CreateSystem(out IntPtr system, uint version);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetOutput(IntPtr system, int output);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDriverCount(IntPtr system, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetDriverInfo(IntPtr system, int index, [Out] byte[] name,
        int length, IntPtr guid, out int rate, out int mode, out int channels);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ReleaseSystem(IntPtr system);
}
