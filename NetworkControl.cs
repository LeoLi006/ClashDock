using System.Text.Json;
namespace ClashDock;

public sealed record NetworkSnapshot(JsonElement Config)
{
    public string Mode => Config.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String ? mode.GetString() ?? "未知" : "未知";
    public bool HasTun => Config.TryGetProperty("tun", out var tun) && tun.ValueKind == JsonValueKind.Object && tun.TryGetProperty("enable", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False;
    public JsonElement Tun => HasTun ? Config.GetProperty("tun") : JsonSerializer.SerializeToElement(new { enable = false });

}
internal static class NetworkGate { internal static readonly SemaphoreSlim Gate = new(1, 1); }
internal static class NetworkConfirmation
{
    internal static bool Ask(IWin32Window? owner, string detail)
    {
        using var dialog = new Form { Text = "确认网络改动", StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(840, 580), MinimumSize = new Size(640, 440), Padding = new Padding(20), ShowInTaskbar = false };
        UiStyle.Window(dialog);
        var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = detail.Replace("\n", "\r\n"), BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.Body };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Math.Max(64, TextRenderer.MeasureText("Ag", Theme.Body).Height + 28), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) };
        var confirm = new PillButton { Text = "确认执行", Accent = true, DialogResult = DialogResult.Yes };
        var cancel = new PillButton { Text = "取消", Outline = true, DialogResult = DialogResult.No };
        footer.Controls.Add(confirm); footer.Controls.Add(cancel); dialog.Controls.Add(text); dialog.Controls.Add(footer);
        dialog.CancelButton = cancel; dialog.Shown += (_, _) => { text.SelectionLength = 0; cancel.Focus(); };
        return dialog.ShowDialog(owner) == DialogResult.Yes;
    }
    internal static void ShowDetails(IWin32Window owner, string message)
    {
        using var dialog = new Form { Text = "完整状态与操作结果", StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(760, 480), MinimumSize = new Size(600, 360), Padding = new Padding(20), ShowInTaskbar = false };
        UiStyle.Window(dialog);
        var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = message.Replace("\n", "\r\n"), BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.Body };
        var close = new PillButton { Text = "关闭", DialogResult = DialogResult.Cancel, Dock = DockStyle.Bottom, Height = Math.Max(44, TextRenderer.MeasureText("Ag", Theme.Body).Height + 16) };
        dialog.Controls.Add(text); dialog.Controls.Add(close); dialog.CancelButton = close; dialog.ShowDialog(owner);
    }
}
