using System.Runtime.InteropServices;

namespace ClashDock;

/// <summary>用于接收全局热键的隐藏消息窗口。</summary>
public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int HotkeyId = 0xB001;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public event EventHandler? HotkeyPressed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());
        // 默认 Ctrl+Alt+Shift+F10
        RegisterHotKey(Handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_SHIFT, 0x79);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            HotkeyPressed?.Invoke(this, EventArgs.Empty);

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        try
        {
            UnregisterHotKey(Handle, HotkeyId);
        }
        catch
        {
            // 忽略
        }
        DestroyHandle();
    }
}
