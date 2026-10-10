using System.Diagnostics;
using System.Runtime.InteropServices;
using osu.Framework.Configuration;
using osu.Framework.Logging;
using osu.Framework.Platform;
using Tomlyn;
using Tomlyn.Model;

namespace LazerRave.Lazer;

internal sealed class DesktopWindowPlacement : IDisposable
{
    private readonly IWindow window;
    private readonly string path;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private Snapshot? saved, pending;
    private IntPtr handle;
    private long nextPoll, pendingSince;
    private bool restored, disposed, startupFitPending;
    private sealed record Snapshot(int X, int Y, int Width, int Height, bool Maximized);

    public DesktopWindowPlacement(IWindow window, string path)
    {
        this.window = window;
        this.path = path;
        window.Update += Update;
        window.ExitRequested += SaveCurrent;
        window.Exited += OnExited;
    }

    private void Update()
    {
        if (clock.ElapsedMilliseconds < nextPoll) return;
        nextPoll = clock.ElapsedMilliseconds + 250;
        if (handle == IntPtr.Zero)
        {
            using var process = Process.GetCurrentProcess();
            handle = process.MainWindowHandle;
        }
        if (handle == IntPtr.Zero || !IsWindowVisible(handle)) return;
        if (!restored)
        {
            restored = true;
            if (window.WindowMode.Value == WindowMode.Windowed)
            {
                Restore();
                FitToWorkArea();
                startupFitPending = true;
            }
            nextPoll = clock.ElapsedMilliseconds + 500;
            return;
        }
        if (startupFitPending)
        {
            // Recheck after SDL has processed restoration and any display/DPI changes.
            FitToWorkArea();
            startupFitPending = false;
        }
        var current = ReadCurrent();
        if (current is null) return;
        if (current != pending)
        {
            pending = current;
            pendingSince = clock.ElapsedMilliseconds;
        }
        else if (current != saved && clock.ElapsedMilliseconds - pendingSince >= 500) Save(current);
    }

    private Snapshot? ReadCurrent()
    {
        if (!restored || window.WindowMode.Value != WindowMode.Windowed || IsIconic(handle)) return null;
        var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
        if (!GetWindowPlacement(handle, ref placement)) return null;
        var rect = placement.Normal;
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return null;
        return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, IsZoomed(handle));
    }

    private void Restore()
    {
        if (!File.Exists(path)) return;
        try
        {
            var data = Toml.ToModel(File.ReadAllText(path));
            static int Number(TomlTable data, string key) => checked((int)(long)data[key]);
            int x = Number(data, "x"), y = Number(data, "y"), width = Number(data, "width"), height = Number(data, "height");
            if (width is < 100 or > 16384 || height is < 100 or > 16384 || Math.Abs((long)x) > 100000 || Math.Abs((long)y) > 100000)
                throw new InvalidDataException("Invalid saved window bounds.");
            bool maximized = (bool)data["maximized"];
            var placement = new Placement
            {
                Length = Marshal.SizeOf<Placement>(), ShowCommand = maximized ? 3 : 1,
                MinX = -1, MinY = -1, MaxX = -1, MaxY = -1,
                Normal = new() { Left = x, Top = y, Right = x + width, Bottom = y + height },
            };
            // rcNormalPosition uses workspace coordinates; passing it back unchanged avoids title-bar and taskbar offsets.
            if (SetWindowPlacement(handle, ref placement)) saved = new(x, y, width, height, maximized);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidCastException or KeyNotFoundException or OverflowException or TomlException)
        {
            Logger.Log("Could not restore window placement: " + error.Message, level: LogLevel.Important);
        }
    }

    private void SaveCurrent()
    {
        if (ReadCurrent() is { } current) Save(current);
    }

    private void FitToWorkArea()
    {
        if (window.WindowMode.Value != WindowMode.Windowed || IsIconic(handle)) return;
        var info = new MonitorInfo { Length = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromWindow(handle, 2), ref info)) return;
        if (info.Work.Right <= info.Work.Left || info.Work.Bottom <= info.Work.Top) return;
        if (IsZoomed(handle))
        {
            var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
            if (!GetWindowPlacement(handle, ref placement)) return;
            int offsetX = info.Work.Left - info.Monitor.Left, offsetY = info.Work.Top - info.Monitor.Top;
            var bounds = placement.Normal;
            bounds.Left += offsetX; bounds.Right += offsetX;
            bounds.Top += offsetY; bounds.Bottom += offsetY;
            var fitted = Fit(bounds, info.Work);
            if (bounds.Equals(fitted)) return;
            fitted.Left -= offsetX; fitted.Right -= offsetX;
            fitted.Top -= offsetY; fitted.Bottom -= offsetY;
            placement.Normal = fitted;
            SetWindowPlacement(handle, ref placement);
        }
        else if (GetWindowRect(handle, out var bounds))
        {
            var fitted = Fit(bounds, info.Work);
            if (!bounds.Equals(fitted)) SetWindowPos(handle, IntPtr.Zero, fitted.Left, fitted.Top,
                fitted.Right - fitted.Left, fitted.Bottom - fitted.Top, 0x0014); // Preserve focus and Z order.
        }
    }

    private static Rect Fit(Rect bounds, Rect area)
    {
        int width = Math.Min(bounds.Right - bounds.Left, area.Right - area.Left);
        int height = Math.Min(bounds.Bottom - bounds.Top, area.Bottom - area.Top);
        int x = Math.Clamp(bounds.Left, area.Left, area.Right - width);
        int y = Math.Clamp(bounds.Top, area.Top, area.Bottom - height);
        return new() { Left = x, Top = y, Right = x + width, Bottom = y + height };
    }

    private void Save(Snapshot current)
    {
        if (current == saved) return;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, Toml.FromModel(new TomlTable
            {
                ["x"] = (long)current.X, ["y"] = (long)current.Y, ["width"] = (long)current.Width,
                ["height"] = (long)current.Height, ["maximized"] = current.Maximized,
            }));
            File.Move(temporary, path, true);
            saved = current;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Logger.Log("Could not save window placement: " + error.Message, level: LogLevel.Important);
        }
    }

    private void OnExited() { SaveCurrent(); Dispose(); }

    public void Dispose()
    {
        if (disposed) return;
        SaveCurrent();
        disposed = true;
        window.Update -= Update;
        window.ExitRequested -= SaveCurrent;
        window.Exited -= OnExited;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Placement
    {
        public int Length, Flags, ShowCommand, MinX, MinY, MaxX, MaxY;
        public Rect Normal;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Length;
        public Rect Monitor, Work;
        public uint Flags;
    }
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr window, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr window, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insert, int x, int y, int width, int height, uint flags);
}
