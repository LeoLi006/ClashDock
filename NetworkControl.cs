using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace ClashDock;

public sealed record NetworkSnapshot(JsonElement Config)
{
    public string Mode => Config.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String ? mode.GetString() ?? "未知" : "未知";
    public bool HasTun => Config.TryGetProperty("tun", out var tun) && tun.ValueKind == JsonValueKind.Object && tun.TryGetProperty("enable", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False;
    public JsonElement Tun => HasTun ? Config.GetProperty("tun") : JsonSerializer.SerializeToElement(new { enable = false });
    public bool Enabled => HasTun && Tun.TryGetProperty("enable", out var value) && value.ValueKind == JsonValueKind.True;
    public bool AutoRoute => HasTun && Tun.TryGetProperty("auto-route", out var value) && value.ValueKind == JsonValueKind.True;
    public string Device => HasTun && Tun.TryGetProperty("device", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    public static bool AdapterUp(NetworkSnapshot snapshot)
    {
        if (!snapshot.Enabled || snapshot.Device.Length == 0) return false;
        try { return NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up && (n.Name.Equals(snapshot.Device, StringComparison.OrdinalIgnoreCase) || n.Description.Equals(snapshot.Device, StringComparison.OrdinalIgnoreCase))); }
        catch (NetworkInformationException) { return false; }
    }
    public string Describe(bool adapterUp) => !HasTun ? "TUN 状态未知：控制器未返回 TUN 配置" : !Enabled ? "TUN 未开启：不遵循系统代理的应用、UDP／游戏流量可能不受应用规则控制" : !adapterUp ? "内核 TUN 开关已开启，但网卡运行未确认；不能认定接管成功" : !AutoRoute ? "TUN 网卡已运行，但自动路由未开启，接管范围可能有限" : $"TUN 网卡已运行 · {Device} · 模式 {Mode}（不等于已实测每个应用流量）";
}
internal static class NetworkGate { internal static readonly SemaphoreSlim Gate = new(1, 1); }
public sealed class TunControl(MihomoClient client, Func<NetworkSnapshot, bool>? adapter = null, Func<Task>? delay = null)
{
    public async Task<string> SetAsync(bool enabled)
    {
        if (!client.IsLocal) throw new InvalidOperationException("TUN 开关仅支持本机控制器，未执行操作。");
        await NetworkGate.Gate.WaitAsync();
        try
        {
            var before = await client.GetNetworkAsync();
            if (!before.HasTun) throw new InvalidOperationException("控制器没有提供 TUN 状态，未执行开关；请在 Verge 检查内核和服务模式。");
            var target = JsonNode.Parse(before.Tun.GetRawText())!.AsObject(); target["enable"] = enabled;
            if (enabled) { target["auto-route"] = true; if (before.Device.Length == 0) target["device"] = "ClashDock"; }
            bool Up(NetworkSnapshot s) => (adapter ?? NetworkSnapshot.AdapterUp)(s);
            try
            {
                await client.PatchConfigAsync(new { mode = enabled ? "rule" : before.Mode, tun = target });
                for (int i = 0; i < 12; i++)
                {
                    var after = await client.GetNetworkAsync();
                    if (after.HasTun && after.Enabled == enabled && (!enabled || (after.Mode == "rule" && after.AutoRoute && Up(after))))
                        return enabled ? "已确认 TUN 开关、自动路由、规则模式及网卡运行；请重连应用验证实际流量。" : "内核已确认 TUN 关闭；未修改应用分流草稿。";
                    await (delay?.Invoke() ?? Task.Delay(500));
                }
                throw new InvalidOperationException(enabled ? "内核未在等待时间内确认 TUN 网卡运行。请检查 Verge 服务模式／管理员权限、网卡驱动及其他 VPN 冲突；未认定开启成功。" : "内核未确认 TUN 已关闭。");
            }
            catch (Exception error)
            {
                try
                {
                    await client.PatchConfigAsync(new { mode = before.Mode, tun = before.Tun });
                    var restored = await client.GetNetworkAsync();
                    if (!Equivalent(before, restored)) throw new IOException("状态不一致");
                }
                catch { throw new InvalidOperationException(error.Message + "\n原 TUN／模式状态恢复未确认，请打开 Verge 检查；不会误报成功。"); }
                throw new InvalidOperationException(error.Message + "\n原 TUN／模式状态已恢复并核验。");
            }
        }
        finally { NetworkGate.Gate.Release(); }
    }
    internal static bool Equivalent(NetworkSnapshot a, NetworkSnapshot b) => a.Mode == b.Mode && a.HasTun == b.HasTun && a.Enabled == b.Enabled && a.AutoRoute == b.AutoRoute && a.Device == b.Device && JsonNode.DeepEquals(JsonNode.Parse(a.Tun.GetRawText()), JsonNode.Parse(b.Tun.GetRawText()));
}
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
    internal static async Task<string> PresetPreview(MihomoClient client, Preset preset)
    {
        var groups = await client.GetSelectorGroupsAsync();
        var lines = new List<string> { $"场景：{preset.Name}", $"模式：{await client.GetModeAsync()} → {(preset.Mode.Length == 0 ? "保持" : preset.Mode)}" };
        foreach (var action in preset.Actions.Where(x => x.Option.Length > 0))
        {
            var group = groups.FirstOrDefault(g => g.Name.Contains(action.GroupKeyword, StringComparison.OrdinalIgnoreCase));
            lines.Add(group != null && group.Options.Contains(action.Option, StringComparer.OrdinalIgnoreCase) ? $"策略组 {group.Name}：{group.Now} → {action.Option}" : $"跳过：{action.GroupKeyword} → {action.Option}（缺少匹配组或选项）");
        }
        return string.Join("\n", lines);
    }
    internal static string Preview(RoutingSettings old, RoutingSettings next, NetworkSnapshot state)
    {
        var rows = new List<string> { $"运行模式：{state.Mode} → rule", "TUN：保持当前状态，不会因保存规则而自动开启", $"默认行为：{old.DefaultBehavior} → {next.DefaultBehavior}", $"代理目标：{next.Proxy}", "" };
        foreach (var pair in new[] { ("应用", old.Applications, next.Applications), ("网站", old.Websites, next.Websites) })
        {
            var keys = pair.Item2.Select(x => x.Target).Union(pair.Item3.Select(x => x.Target), StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                var before = pair.Item2.FirstOrDefault(x => x.Target.Equals(key, StringComparison.OrdinalIgnoreCase))?.Mode ?? "默认";
                var after = pair.Item3.FirstOrDefault(x => x.Target.Equals(key, StringComparison.OrdinalIgnoreCase))?.Mode ?? "默认";
                if (before != after) rows.Add($"{pair.Item1} {Path.GetFileName(key)}：{before} → {after}");
            }
        }
        rows.Add("\n网站规则优先于应用；明确选自动的应用沿用订阅。默认直连会让其余未单设流量直接连接。保留原订阅规则和全局脚本备份；失败尝试恢复并核验。已有连接需重新建立。");
        return string.Join("\n", rows);
    }
}
