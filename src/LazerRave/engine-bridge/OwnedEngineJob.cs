using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LazerRave.Bridge;

internal sealed class OwnedEngineJob : SafeHandleZeroOrMinusOneIsInvalid
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int informationClass, ref ExtendedLimits limits, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    public OwnedEngineJob(Process process) : base(true)
    {
        SetHandle(CreateJobObjectW(IntPtr.Zero, null));
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
        if (IsInvalid || !SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()) ||
            (!process.HasExited && !AssignProcessToJobObject(handle, process.Handle)))
        {
            int error = Marshal.GetLastWin32Error();
            if (process.HasExited) return;
            if (!process.HasExited) process.Kill(true);
            Dispose(); throw new Win32Exception(error, "Cannot contain the embedded game process.");
        }
    }
    protected override bool ReleaseHandle() => CloseHandle(handle);
}
