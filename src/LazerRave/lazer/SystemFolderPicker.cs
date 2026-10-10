using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using osu.Framework.Platform;
using SDL;

namespace LazerRave.Lazer;

internal static unsafe class SystemFolderPicker
{
    private sealed class Request(string initialPath)
    {
        public readonly TaskCompletionSource<string?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly IntPtr InitialPath = Marshal.StringToCoTaskMemUTF8(initialPath);
    }

    public static Task<string?> Show(GameHost host, string initialPath)
    {
        var request = new Request(initialPath);
        host.InputThread.Scheduler.Add(() =>
        {
            var handle = GCHandle.Alloc(request);
            try
            {
                SDL3.SDL_ShowOpenFolderDialog(&Completed, GCHandle.ToIntPtr(handle), SDL3.SDL_GetKeyboardFocus(), (byte*)request.InitialPath, false);
            }
            catch (Exception error)
            {
                handle.Free();
                Marshal.FreeCoTaskMem(request.InitialPath);
                request.Completion.TrySetException(error);
            }
        });
        return request.Completion.Task;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Completed(IntPtr userData, byte** files, int filter)
    {
        var handle = GCHandle.FromIntPtr(userData);
        var request = (Request)handle.Target!;
        try
        {
            if (files == null) request.Completion.TrySetException(new IOException(SDL3.SDL_GetError()));
            else request.Completion.TrySetResult(*files == null ? null : Marshal.PtrToStringUTF8((IntPtr)(*files)));
        }
        catch (Exception error) { request.Completion.TrySetException(error); }
        finally
        {
            Marshal.FreeCoTaskMem(request.InitialPath);
            handle.Free();
        }
    }
}
