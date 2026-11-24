using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowLabels
{
    internal sealed class WindowCandidate
    {
        internal readonly IntPtr Handle;
        internal readonly int ProcessId;
        internal readonly uint ThreadId;
        internal readonly string Title;
        internal readonly string ProcessName;
        internal readonly Rectangle Bounds;

        internal WindowCandidate(IntPtr handle, int processId, uint threadId,
            string title, string processName, Rectangle bounds)
        {
            Handle = handle;
            ProcessId = processId;
            ThreadId = threadId;
            Title = title;
            ProcessName = processName;
            Bounds = bounds;
        }
    }

    internal sealed class WindowBinding
    {
        internal readonly WindowCandidate Candidate;
        internal readonly string PropertyName;

        internal WindowBinding(WindowCandidate candidate, string propertyName)
        {
            Candidate = candidate;
            PropertyName = propertyName;
        }
    }

    internal enum WindowBindingError
    {
        None,
        WindowClosed,
        AccessDenied,
        Rejected
    }

    internal sealed class DesktopSnapshot
    {
        internal readonly List<DesktopWindow> Windows;

        internal DesktopSnapshot(List<DesktopWindow> windows)
        {
            Windows = windows;
        }
    }

    internal struct DesktopWindow
    {
        internal readonly IntPtr Handle;
        internal readonly Rectangle Bounds;

        internal DesktopWindow(IntPtr handle, Rectangle bounds)
        {
            Handle = handle;
            Bounds = bounds;
        }
    }

    internal static class NativeWindows
    {
        internal const int ExtendedTransparent = 0x00000020;
        internal const int ExtendedToolWindow = 0x00000080;
        internal const int ExtendedLayered = 0x00080000;
        internal const int ExtendedNoActivate = 0x08000000;

        private const int ExtendedAppWindow = 0x00040000;
        private const int ExtendedStyleIndex = -20;
        private const uint LayerAlpha = 0x00000002;
        private const int EmptyWindowRegion = 1;
        private const int ExtendedFrameBoundsAttribute = 9;
        private const int CloakedAttribute = 14;
        private const uint NextWindow = 2;
        private const uint OwnerWindow = 4;
        private const uint RootAncestor = 2;
        private const uint NearestMonitor = 2;
        private const int RestoreWindow = 9;
        private const int DefaultDpi = 96;
        private const int AccessDeniedHResult = unchecked((int)0x80070005);

        private static readonly IntPtr BindingMarker = new IntPtr(1);
        private static readonly int OwnProcessId = GetOwnProcessId();

        internal static void InstallDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4)) || Marshal.GetLastWin32Error() == 5)
                    return;
            }
            catch (EntryPointNotFoundException) { }

            try
            {
                int result = SetProcessDpiAwareness(2);
                if (result == 0 || result == AccessDeniedHResult)
                    return;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            SetProcessDPIAware();
        }

        internal static List<WindowCandidate> EnumerateCandidates()
        {
            var candidates = new List<WindowCandidate>();
            var processNames = new Dictionary<int, string>();
            foreach (IntPtr handle in EnumerateInZOrder())
            {
                WindowCandidate candidate = ReadCandidate(handle, processNames);
                if (candidate != null)
                    candidates.Add(candidate);
            }
            return candidates;
        }

        internal static WindowCandidate GetForegroundCandidate()
        {
            return ResolveWindowCandidate(GetForegroundWindow());
        }

        internal static WindowCandidate GetCandidateAt(Point point)
        {
            return ResolveWindowCandidate(WindowFromPoint(new NativePoint { X = point.X, Y = point.Y }));
        }

        private static WindowCandidate ResolveWindowCandidate(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return null;

            IntPtr current = GetAncestor(handle, RootAncestor);
            uint processId;
            if (GetWindowThreadProcessId(current, out processId) == 0 || (int)processId == OwnProcessId)
                return null;

            // Route owned dialogs to the same application's main window.
            var visited = new HashSet<IntPtr>();
            var processNames = new Dictionary<int, string>();
            WindowCandidate candidate = null;
            while (current != IntPtr.Zero && visited.Add(current))
            {
                uint ownerProcessId;
                if (GetWindowThreadProcessId(current, out ownerProcessId) == 0 || ownerProcessId != processId)
                    break;

                WindowCandidate ownerCandidate = ReadCandidate(current, processNames);
                if (ownerCandidate != null)
                {
                    candidate = ownerCandidate;
                    if ((GetWindowLong(current, ExtendedStyleIndex) & ExtendedAppWindow) != 0)
                        break;
                }
                current = GetWindow(current, OwnerWindow);
            }
            return candidate;
        }

        internal static bool TryBind(WindowCandidate candidate, out WindowBinding binding)
        {
            WindowBindingError error;
            return TryBind(candidate, out binding, out error);
        }

        internal static bool TryBind(WindowCandidate candidate, out WindowBinding binding, out WindowBindingError error)
        {
            binding = null;
            error = WindowBindingError.WindowClosed;
            if (!HasSameWindowThread(candidate))
                return false;

            // The property belongs to this particular window, even if Windows reuses its handle.
            string propertyName = "WindowLabels." + Guid.NewGuid().ToString("N");
            if (!SetProp(candidate.Handle, propertyName, BindingMarker))
            {
                int code = Marshal.GetLastWin32Error();
                error = code == 5 ? WindowBindingError.AccessDenied : WindowBindingError.Rejected;
                return false;
            }

            var newBinding = new WindowBinding(candidate, propertyName);
            if (!IsAlive(newBinding))
            {
                RemoveProp(candidate.Handle, propertyName);
                return false;
            }

            binding = newBinding;
            error = WindowBindingError.None;
            return true;
        }

        internal static bool IsAlive(WindowBinding binding)
        {
            return HasSameWindowThread(binding.Candidate)
                && GetProp(binding.Candidate.Handle, binding.PropertyName) == BindingMarker;
        }

        internal static void Release(WindowBinding binding)
        {
            if (IsAlive(binding))
                RemoveProp(binding.Candidate.Handle, binding.PropertyName);
        }

        internal static bool TryActivate(WindowCandidate candidate)
        {
            if (!HasSameWindowThread(candidate))
                return false;

            if (IsIconic(candidate.Handle))
                ShowWindowAsync(candidate.Handle, RestoreWindow);

            return SetForegroundWindow(candidate.Handle);
        }

        internal static bool TryGetVisibleBounds(WindowBinding binding, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            return IsAlive(binding) && TryGetDisplayBounds(binding.Candidate.Handle, out bounds);
        }

        internal static int GetDpi(WindowBinding binding)
        {
            IntPtr handle = binding.Candidate.Handle;
            IntPtr monitor = MonitorFromWindow(handle, NearestMonitor);
            if (monitor != IntPtr.Zero)
            {
                try
                {
                    int scale;
                    if (GetScaleFactorForMonitor(monitor, out scale) == 0 && scale > 0)
                        return (DefaultDpi * scale + 50) / 100;
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            }

            try
            {
                uint dpi = GetDpiForWindow(handle);
                if (dpi > 0)
                    return (int)dpi;
            }
            catch (EntryPointNotFoundException) { }
            return DefaultDpi;
        }

        internal static DesktopSnapshot CaptureDesktop(IntPtr ownOccluder = default(IntPtr))
        {
            var windows = new List<DesktopWindow>();
            IntPtr desktop = GetDesktopWindow();
            IntPtr shell = GetShellWindow();
            foreach (IntPtr handle in EnumerateInZOrder())
            {
                uint processId;
                if (GetWindowThreadProcessId(handle, out processId) == 0
                    || ((int)processId == OwnProcessId && handle != ownOccluder))
                    continue;

                Rectangle bounds;
                if (TryGetDisplayBounds(handle, out bounds)
                    && !IsNonOccludingLayer(handle)
                    && !IsDesktopSurface(handle, desktop, shell, ReadClassName(handle)))
                    windows.Add(new DesktopWindow(handle, bounds));
            }

            return new DesktopSnapshot(windows);
        }

        internal static bool CanPlaceBadge(DesktopSnapshot desktop, WindowBinding binding, Rectangle badgeBounds)
        {
            if (badgeBounds.Width <= 0 || badgeBounds.Height <= 0)
                return false;

            foreach (DesktopWindow window in desktop.Windows)
            {
                if (window.Handle == binding.Candidate.Handle)
                    return window.Bounds.Contains(badgeBounds);

                if (window.Bounds.IntersectsWith(badgeBounds))
                    return false;
            }

            return false;
        }

        private static bool HasSameWindowThread(WindowCandidate candidate)
        {
            uint processId;
            uint threadId = GetWindowThreadProcessId(candidate.Handle, out processId);
            return threadId != 0 && threadId == candidate.ThreadId && (int)processId == candidate.ProcessId;
        }

        private static IEnumerable<IntPtr> EnumerateInZOrder()
        {
            // A moving or closing window can change the list while it is being read.
            var visited = new HashSet<IntPtr>();
            IntPtr handle = GetTopWindow(IntPtr.Zero);
            while (handle != IntPtr.Zero && visited.Add(handle))
            {
                IntPtr next = GetWindow(handle, NextWindow);
                yield return handle;
                handle = next;
            }
        }

        private static WindowCandidate ReadCandidate(IntPtr handle, Dictionary<int, string> processNames)
        {
            uint nativeProcessId;
            uint threadId = GetWindowThreadProcessId(handle, out nativeProcessId);
            int processId = (int)nativeProcessId;
            if (threadId == 0 || processId == OwnProcessId || !IsWindowVisible(handle) || IsCloaked(handle))
                return null;

            int styles = GetWindowLong(handle, ExtendedStyleIndex);
            if ((styles & (ExtendedToolWindow | ExtendedNoActivate)) != 0 && (styles & ExtendedAppWindow) == 0)
                return null;

            string className = ReadClassName(handle);
            if (IsDesktopSurface(handle, GetDesktopWindow(), GetShellWindow(), className)
                || className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd")
                return null;

            var titleBuffer = new StringBuilder(512);
            GetWindowText(handle, titleBuffer, titleBuffer.Capacity);
            string title = titleBuffer.ToString().Trim();

            Rectangle bounds = Rectangle.Empty;
            if (!IsIconic(handle) && !TryGetFrameBounds(handle, out bounds))
                return null;

            if (title.Length == 0)
            {
                // Some applications draw their entire title bar without setting a window caption.
                bool appWindow = (styles & ExtendedAppWindow) != 0;
                bool mainSurface = GetWindow(handle, OwnerWindow) == IntPtr.Zero
                    && bounds.Width >= 180 && bounds.Height >= 100;
                if (!appWindow && !mainSurface)
                    return null;
                title = "未命名窗口";
            }

            string processName;
            if (!processNames.TryGetValue(processId, out processName))
            {
                processName = ReadProcessName(processId);
                processNames.Add(processId, processName);
            }

            return new WindowCandidate(handle, processId, threadId, title, processName, bounds);
        }

        private static bool IsDesktopSurface(IntPtr handle, IntPtr desktop, IntPtr shell, string className)
        {
            return handle == desktop || handle == shell || className == "Progman" || className == "WorkerW";
        }

        private static bool IsNonOccludingLayer(IntPtr handle)
        {
            // A HUD can have a monitor-sized window with almost all of it transparent.
            int styles = GetWindowLong(handle, ExtendedStyleIndex);
            if ((styles & ExtendedLayered) == 0)
                return false;
            if ((styles & ExtendedTransparent) != 0)
                return true;

            uint colorKey;
            byte alpha;
            uint flags;
            NativeRectangle region;
            return (GetLayeredWindowAttributes(handle, out colorKey, out alpha, out flags)
                && (flags & LayerAlpha) != 0 && alpha == 0)
                || GetWindowRgnBox(handle, out region) == EmptyWindowRegion;
        }

        private static string ReadClassName(IntPtr handle)
        {
            var className = new StringBuilder(256);
            GetClassName(handle, className, className.Capacity);
            return className.ToString();
        }

        private static string ReadProcessName(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                    return process.ProcessName;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            return string.Empty;
        }

        private static int GetOwnProcessId()
        {
            using (Process process = Process.GetCurrentProcess())
                return process.Id;
        }

        private static bool TryGetDisplayBounds(IntPtr handle, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            return IsWindowVisible(handle) && !IsIconic(handle) && !IsCloaked(handle)
                && TryGetFrameBounds(handle, out bounds);
        }

        private static bool TryGetFrameBounds(IntPtr handle, out Rectangle bounds)
        {
            NativeRectangle rect;
            if ((DwmGetWindowAttribute(handle, ExtendedFrameBoundsAttribute, out rect, 4 * sizeof(int)) != 0
                || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                && !GetWindowRect(handle, out rect))
            {
                bounds = Rectangle.Empty;
                return false;
            }

            bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        private static bool IsCloaked(IntPtr handle)
        {
            int cloaked;
            return DwmGetWindowAttribute(handle, CloakedAttribute, out cloaked, sizeof(int)) == 0 && cloaked != 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetScaleFactorForMonitor(IntPtr monitor, out int scale);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr handle, uint command);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(NativePoint point);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr handle, int command);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowLong(IntPtr handle, int index);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr handle, StringBuilder className, int count);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr handle, out NativeRectangle rect);

        [DllImport("user32.dll")]
        private static extern bool GetLayeredWindowAttributes(IntPtr handle, out uint colorKey, out byte alpha, out uint flags);

        [DllImport("user32.dll")]
        private static extern int GetWindowRgnBox(IntPtr handle, out NativeRectangle rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out NativeRectangle result, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int result, int size);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetProp(IntPtr handle, string name, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetProp(IntPtr handle, string name);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr RemoveProp(IntPtr handle, string name);
    }
}
