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
        const int ExtendedStyle=-20;
        const long NoActivate=0x08000000;
        readonly Window window;
        readonly Button insert;
        HwndSource? source;
        bool originallyNoActivate;

        public Attachment(Window window, Button insert)
        {
            this.window = window;
            this.insert = insert;
            insert.Focusable = false;
            window.SourceInitialized += SourceInitialized;
            window.Closed += Closed;
            window.Activated += Activated;
            insert.MouseLeave += LeaveInsert;
            insert.IsEnabledChanged += AvailabilityChanged;
            insert.IsVisibleChanged += AvailabilityChanged;
            insert.IsHitTestVisibleChanged += AvailabilityChanged;
            AttachSource();
        }

        void SourceInitialized(object? sender, EventArgs args) => AttachSource();
        void Activated(object? sender, EventArgs args)
        {
            if(source is {} current && WindowsTextDelivery.ForegroundWindow==current.Handle)
                SetNoActivate(false);
        }
        void LeaveInsert(object sender,System.Windows.Input.MouseEventArgs args)=>SetNoActivate(false);
        void AvailabilityChanged(object sender,DependencyPropertyChangedEventArgs args)
        {
            if(!insert.IsEnabled||!insert.IsVisible||!insert.IsHitTestVisible)SetNoActivate(false);
        }

        void AttachSource()
        {
            if (source is not null) return;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == 0 || HwndSource.FromHwnd(handle) is not { } current) return;
            source = current;
            originallyNoActivate=((long)GetWindowLongPtr(handle,ExtendedStyle)&NoActivate)!=0;
            source.AddHook(WindowMessage);
        }

        void Closed(object? sender, EventArgs args)
        {
            window.SourceInitialized -= SourceInitialized;
            window.Closed -= Closed;
            window.Activated -= Activated;
            insert.MouseLeave -= LeaveInsert;
            insert.IsEnabledChanged -= AvailabilityChanged;
            insert.IsVisibleChanged -= AvailabilityChanged;
            insert.IsHitTestVisibleChanged -= AvailabilityChanged;
            if (source is { IsDisposed: false })
            {
                SetNoActivate(false);
                source.RemoveHook(WindowMessage);
            }
            source = null;
        }

        nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            const int WmNcHitTest=0x84,WmMouseActivate=0x21,WmLeftButtonDown=0x201,MaNoActivate=3;
            if(message==WmNcHitTest)
            {
                // Set the native style before Windows starts mouse activation. Returning
                // MA_NOACTIVATE alone can leave another process with no foreground window.
                var packed=lParam.ToInt64();
                SetNoActivate(ShouldPreserve(hwnd,new ScreenPoint{X=(short)(packed&0xffff),Y=(short)((packed>>16)&0xffff)}));
                return 0; // Keep normal hit-testing and editing behavior outside Insert.
            }
            if(message!=WmMouseActivate||((lParam.ToInt64()>>16)&0xffff)!=WmLeftButtonDown)
                return 0;
            if(!GetCursorPos(out var pointer)||!ShouldPreserve(hwnd,pointer))return 0;
            SetNoActivate(true);
            handled=true;
            return MaNoActivate;
        }

        // WPF IsActive can remain true during a non-activating click while another
        // process owns the foreground. Use the same native fact as cursor capture.
        bool ShouldPreserve(nint hwnd,ScreenPoint pointer)=>
            SelectedCaretActivationPolicy.ShouldPreserveExternalFocus(
                WindowsTextDelivery.ForegroundWindow==hwnd,insert.IsEnabled,
                insert.IsVisible&&insert.IsHitTestVisible,PointerHitsInsert(pointer));

        void SetNoActivate(bool preserve)
        {
            if(source is not {IsDisposed:false} current)return;
            var style=(long)GetWindowLongPtr(current.Handle,ExtendedStyle);
            var next=preserve||originallyNoActivate?style|NoActivate:style&~NoActivate;
            if(next!=style)SetWindowLongPtr(current.Handle,ExtendedStyle,(nint)next);
        }

        bool PointerHitsInsert(ScreenPoint pointer)
        {
            try
            {
                // WPF converts physical screen coordinates, including monitor DPI.
                var point=window.PointFromScreen(new(pointer.X,pointer.Y));
                var hit=window.InputHitTest(point) as DependencyObject;
                for(var current=hit;current is not null;
                    current=current is Visual?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current))
                    if(ReferenceEquals(current,insert))return true;
            }
            catch(InvalidOperationException)
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
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]
    static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]
    static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
}
