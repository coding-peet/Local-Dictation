using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using LocalDictation.Core;

namespace LocalDictation.App.Services;

/// <summary>Preserves the selected external caret for an explicit Insert click.</summary>
public static class SelectedCaretInsertActivation
{
    public static void Attach(Window window, Button insert)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(insert);
        _ = new Attachment(window, insert);
    }

    sealed class Attachment
    {
        readonly Window window;
        readonly Button insert;
        HwndSource? source;

        public Attachment(Window window, Button insert)
        {
            this.window = window;
            this.insert = insert;
            insert.Focusable = false;
            window.SourceInitialized += SourceInitialized;
            window.Closed += Closed;
            AttachSource();
        }

        void SourceInitialized(object? sender, EventArgs args) => AttachSource();

        void AttachSource()
        {
            if (source is not null) return;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == 0 || HwndSource.FromHwnd(handle) is not { } current) return;
            source = current;
            source.AddHook(WindowMessage);
        }

        void Closed(object? sender, EventArgs args)
        {
            window.SourceInitialized -= SourceInitialized;
            window.Closed -= Closed;
            if (source is { IsDisposed: false }) source.RemoveHook(WindowMessage);
            source = null;
        }

        nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            const int WmMouseActivate = 0x21, WmLeftButtonDown = 0x201, MaNoActivate = 3;
            if (message != WmMouseActivate || ((lParam.ToInt64() >> 16) & 0xffff) != WmLeftButtonDown)
                return 0;
            if (window.IsActive || !insert.IsEnabled || !insert.IsVisible || !insert.IsHitTestVisible)
                return 0;
            if (!SelectedCaretActivationPolicy.ShouldPreserveExternalFocus(window.IsActive,
                    insert.IsEnabled, insert.IsVisible && insert.IsHitTestVisible, PointerHitsInsert()))
                return 0;

            handled = true;
            return MaNoActivate;
        }

        bool PointerHitsInsert()
        {
            if (!GetCursorPos(out var pointer)) return false;
            try
            {
                // WPF performs the screen-to-layout conversion, including monitor DPI.
                var point = window.PointFromScreen(new(pointer.X, pointer.Y));
                var hit = window.InputHitTest(point) as DependencyObject;
                for (var current = hit; current is not null;
                     current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
                    if (ReferenceEquals(current, insert)) return true;
            }
            catch (InvalidOperationException)
            {
                // A disconnected presentation source retains normal activation.
            }
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ScreenPoint { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetCursorPos(out ScreenPoint point);
}
