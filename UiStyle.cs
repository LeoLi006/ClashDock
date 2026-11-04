using System.Runtime.InteropServices;
namespace ClashDock;

internal static class UiStyle
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    public static void Window(Form form)
    {
        form.BackColor = Theme.Bg; form.ForeColor = Theme.Text; form.Font = Theme.Body;
        form.HandleCreated += (_, _) => { int dark = 1; try { DwmSetWindowAttribute(form.Handle, 20, ref dark, 4); } catch (DllNotFoundException) { } };
    }
    public static void Input(Control control)
    {
        control.BackColor = Theme.Input; control.ForeColor = Theme.Text; control.Font = Theme.Body;
        if (control is ComboBox combo) combo.FlatStyle = FlatStyle.Flat;
        if (control is TextBox text) text.BorderStyle = BorderStyle.FixedSingle;
    }
}
