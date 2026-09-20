using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PerDeviceMixer.App;

public partial class DeviceSwitchToastWindow : Window
{
    private const int WindowLongExtendedStyle = -20;
    private const int ExtendedStyleNoActivate = 0x08000000;
    private const int ExtendedStyleTransparent = 0x00000020;

    internal DeviceSwitchToastWindow() => InitializeComponent();

    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var extendedStyle = GetWindowLongPtr(handle, WindowLongExtendedStyle).ToInt64();
        extendedStyle |= ExtendedStyleNoActivate | ExtendedStyleTransparent;
        _ = SetWindowLongPtr(handle, WindowLongExtendedStyle, new IntPtr(extendedStyle));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        MakeClickThrough();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
