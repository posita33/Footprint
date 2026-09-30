using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Footprint.Core;

namespace Footprint;

internal static class WindowPosition
{
    public static WindowPlacement Capture(Window window)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
        return new WindowPlacement
        {
            Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height,
            Maximized = window.WindowState == WindowState.Maximized
        };
    }

    public static void Restore(Window window, WindowPlacement? saved)
    {
        if (saved is null || !saved.IsValid) return;
        // Monitor APIs use physical pixels; WPF window bounds use device-independent units.
        var scale = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var rect = new NativeRect
        {
            Left = (int)(saved.Left * scale.M11), Top = (int)(saved.Top * scale.M22),
            Right = (int)((saved.Left + saved.Width) * scale.M11),
            Bottom = (int)((saved.Top + saved.Height) * scale.M22)
        };
        var monitor = MonitorFromRect(ref rect, 2); // nearest monitor if the saved screen was removed
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var work = SystemParameters.WorkArea;
        if (GetMonitorInfo(monitor, ref info))
            work = new Rect(info.Work.Left / scale.M11, info.Work.Top / scale.M22,
                (info.Work.Right - info.Work.Left) / scale.M11, (info.Work.Bottom - info.Work.Top) / scale.M22);
        window.Width = Math.Max(window.MinWidth, Math.Min(saved.Width, work.Width));
        window.Height = Math.Max(window.MinHeight, Math.Min(saved.Height, work.Height));
        window.Left = Math.Clamp(saved.Left, work.Left, Math.Max(work.Left, work.Right - window.Width));
        window.Top = Math.Clamp(saved.Top, work.Top, Math.Max(work.Top, work.Bottom - window.Height));
        if (saved.Maximized) window.WindowState = WindowState.Maximized;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
