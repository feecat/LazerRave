using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using osu.Framework.Platform;

namespace LazerRave.Bridge;

internal sealed record EmbedTarget(ulong Window, uint Process);
internal readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static PixelRect Fit(PixelRect area, int width, int height)
    {
        double scale = Math.Min((double)Math.Max(1, area.Width) / width, (double)Math.Max(1, area.Height) / height);
        int fittedWidth = Math.Max(1, (int)Math.Floor(width * scale)), fittedHeight = Math.Max(1, (int)Math.Floor(height * scale));
        return new(area.X + (area.Width - fittedWidth) / 2, area.Y + (area.Height - fittedHeight) / 2, fittedWidth, fittedHeight);
    }
}

internal sealed class NativeGameViewport : IDisposable
{
    private readonly IWindow? window;
    private readonly IntPtr parentOverride;
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> commands = new();
    private readonly NativeWindowApi.WindowProcedure procedure;
    private IntPtr parent, child, originalProcedure, engine;
    private volatile Layout layout = new(new(0, 0, 640, 480), 640, 480);
    private PixelRect lastBounds;
    private int disposed;
    private sealed record Layout(PixelRect Area, int Width, int Height);
    public NativeGameViewport(IWindow? window, IntPtr parentOverride = default)
    {
        this.window = window; this.parentOverride = parentOverride; procedure = WindowMessage;
        if (window is not null) { window.Update += Pump; window.Exited += OnWindowExited; }
    }
    public Task<EmbedTarget> OpenAsync(PixelRect area, int width, int height, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        SetBounds(area, width, height);
        var completion = new TaskCompletionSource<EmbedTarget>(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Enqueue(() =>
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (disposed != 0) throw new ObjectDisposedException(nameof(NativeGameViewport));
                parent = parentOverride != IntPtr.Zero ? parentOverride : Process.GetCurrentProcess().MainWindowHandle;
                if (!NativeWindowApi.IsWindow(parent)) throw new IOException("The frontend window is unavailable.");
                if (child != IntPtr.Zero) throw new InvalidOperationException("An embedded session is already open.");
                long style = NativeWindowApi.GetWindowLongPtrW(parent, -16).ToInt64();
                NativeWindowApi.SetWindowLongPtrW(parent, -16, new(style | 0x02000000));
                var previousDpi = NativeWindowApi.SetThreadDpiAwarenessContext(NativeWindowApi.GetWindowDpiAwarenessContext(parent));
                try
                {
                    child = NativeWindowApi.CreateWindowExW(0, "STATIC", "LazerRave gameplay", 0x56000004,
                        0, 0, 1, 1, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    if (child == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    originalProcedure = NativeWindowApi.SetWindowLongPtrW(child, -4, Marshal.GetFunctionPointerForDelegate(procedure));
                }
                finally { if (previousDpi != IntPtr.Zero) NativeWindowApi.SetThreadDpiAwarenessContext(previousDpi); }
                lastBounds = default; UpdateBounds();
                if (NativeWindowApi.IsWindowVisible(child)) NativeWindowApi.SetFocus(child);
                completion.SetResult(new(unchecked((ulong)child.ToInt64()), (uint)Environment.ProcessId));
            }
            catch (Exception error) { CloseOnWindowThread(); completion.SetException(error); }
        });
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellation);
    }
    public void SetBounds(PixelRect area, int width, int height) => layout = new(area, width, height);
    public void BindEngine(IntPtr handle, int process)
    {
        commands.Enqueue(() =>
        {
            if (child != IntPtr.Zero && NativeWindowApi.IsWindow(handle) &&
                NativeWindowApi.GetWindowThreadProcessId(handle, out uint owner) != 0 && owner == (uint)process)
                engine = handle;
        });
    }
    public Task CloseAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Enqueue(() => { CloseOnWindowThread(); completion.TrySetResult(); });
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    internal void Pump()
    {
        while (commands.TryDequeue(out var command)) command();
        if (child != IntPtr.Zero && NativeWindowApi.IsWindow(child)) UpdateBounds();
    }
    private void UpdateBounds()
    {
        var current = layout;
        if (!NativeWindowApi.GetClientRect(parent, out var client)) return;
        var area = current.Area;
        area = new(Math.Max(0, area.X), Math.Max(0, area.Y),
            Math.Max(1, Math.Min(area.Width, client.Right - Math.Max(0, area.X))),
            Math.Max(1, Math.Min(area.Height, client.Bottom - Math.Max(0, area.Y))));
        var fitted = PixelRect.Fit(area, current.Width, current.Height);
        if (fitted == lastBounds) return;
        NativeWindowApi.SetWindowPos(child, IntPtr.Zero, fitted.X, fitted.Y, fitted.Width, fitted.Height, 0x0014);
        lastBounds = fitted;
    }
    private IntPtr WindowMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam)
    {
        if (message == 0x0014) return new(1); // DxLib owns the viewport contents.
        if (message == 0x0020) { NativeWindowApi.SetCursor(IntPtr.Zero); return new(1); }
        if (message == 0x0201) NativeWindowApi.SetFocus(hwnd);
        if (engine != IntPtr.Zero && message is 0x020A or 0x0100 or 0x0101 or 0x0102)
            NativeWindowApi.PostMessageW(engine, message, wparam, lparam);
        return NativeWindowApi.CallWindowProcW(originalProcedure, hwnd, message, wparam, lparam);
    }
    private void CloseOnWindowThread()
    {
        if (child != IntPtr.Zero && NativeWindowApi.IsWindow(child))
        {
            NativeWindowApi.SetWindowLongPtrW(child, -4, originalProcedure);
            NativeWindowApi.DestroyWindow(child);
        }
        child = engine = originalProcedure = IntPtr.Zero;
        if (NativeWindowApi.GetForegroundWindow() == parent) NativeWindowApi.SetFocus(parent);
    }
    private void OnWindowExited()
    {
        CloseOnWindowThread();
        if (window is not null) { window.Update -= Pump; window.Exited -= OnWindowExited; }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        commands.Enqueue(OnWindowExited);
        if (window is null) Pump();
    }
}

internal static class NativeWindowApi
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr CreateWindowExW(uint extended, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);
    [DllImport("user32.dll")] internal static extern IntPtr SetWindowLongPtrW(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr insert, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] internal static extern bool PostMessageW(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] internal static extern IntPtr CallWindowProcW(IntPtr procedure, IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref Message message);
    internal static void PumpMessages() { while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessageW(ref message); } }
}
