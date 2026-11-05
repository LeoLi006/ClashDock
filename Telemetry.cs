using System.Text.Json;
namespace ClashDock;
public sealed record TargetStatus(string Name, string Selected, int? Delay, DateTimeOffset? Time)
{
    public string Summary => (Selected.Length > 0 ? "当前：" + Selected + " · " : "") + (Delay == null ? "未测速" : Delay == 0 ? "超时" : Delay + " ms") + (Time == null ? " · 时间未知" : " · " + Time.Value.ToLocalTime().ToString("MM-dd HH:mm:ss") + (DateTimeOffset.Now - Time > TimeSpan.FromMinutes(10) ? "（陈旧）" : ""));
}
public sealed partial class MihomoClient
{
    public async Task<List<TargetStatus>> GetTargetsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(_base + "/proxies", ct); await EnsureAsync(response);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var map = doc.RootElement.GetProperty("proxies"); var result = new List<TargetStatus>();
        foreach (var p in map.EnumerateObject())
        {
            if (p.Name == "GLOBAL" || p.Name.StartsWith("__ClashDock_") || p.Value.GetProperty("type").GetString() is "Direct" or "Reject" or "Pass" or "Compatible") continue;
            string selected = p.Value.TryGetProperty("now", out var now) ? now.GetString() ?? "" : "";
            var v = p.Value; var seen = new HashSet<string>(); var selectionChain = new List<string>();
            while (v.TryGetProperty("now", out var next) && next.GetString() is string name && seen.Add(name) && map.TryGetProperty(name, out var child)) { selectionChain.Add(name); v = child; }
            if (selectionChain.Count > 0) selected = string.Join(" → ", selectionChain);
            int? delay = null; DateTimeOffset? time = null;
            if (v.TryGetProperty("history", out var history) && history.ValueKind == JsonValueKind.Array && history.GetArrayLength() > 0)
            {
                var last = history[history.GetArrayLength()-1]; if (last.TryGetProperty("delay", out var d) && d.TryGetInt32(out var n) && n >= 0) delay = n;
                if (last.TryGetProperty("time", out var t) && DateTimeOffset.TryParse(t.GetString(), out var parsed)) time = parsed;
            }
            result.Add(new(p.Name, selected, delay, time));
        }
        return result.OrderBy(x => x.Name).ToList();
    }
    public async Task<string> MeasureAsync(string name, CancellationToken ct)
    {
        try {
            using var response = await _http.GetAsync(_base + "/proxies/" + Uri.EscapeDataString(name) + "/delay?timeout=5000&url=" + Uri.EscapeDataString("https://www.gstatic.com/generate_204"), ct);
            if (!response.IsSuccessStatusCode) {
                if (response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout) return "节点测速超时 · " + DateTime.Now.ToString("HH:mm:ss");
                await EnsureAsync(response);
            }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            int delay = doc.RootElement.GetProperty("delay").GetInt32(); return (delay == 0 ? "节点超时" : delay + " ms") + " · " + DateTime.Now.ToString("HH:mm:ss");
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) { return "已取消"; }
        catch (HttpRequestException) { return "控制器连接失败（不是节点超时）"; }
        catch (Exception e) when (e is InvalidOperationException or JsonException or TaskCanceledException or KeyNotFoundException or FormatException) { return "测速失败 · " + e.Message; }
    }
    public async Task<string> DiagnoseAsync(string target, bool app)
    {
        using var response = await _http.GetAsync(_base + "/connections"); await EnsureAsync(response);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var lines = new List<string>();
        foreach (var c in doc.RootElement.GetProperty("connections").EnumerateArray()) {
            var m = c.GetProperty("metadata"); string S(JsonElement e, string key) => e.TryGetProperty(key, out var value) ? value.ToString() : "";
            string path = S(m, "processPath"), process = S(m, "process"), host = S(m, "host");
            bool match = app ? (target.Contains('\\') || target.Contains('/') ? path.Equals(target, StringComparison.OrdinalIgnoreCase) : Path.GetFileName(path).Equals(target, StringComparison.OrdinalIgnoreCase) || process.Equals(target, StringComparison.OrdinalIgnoreCase)) : host.Equals(target, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + target, StringComparison.OrdinalIgnoreCase);
            if (!match) continue;
            var chains = c.TryGetProperty("chains", out var chain) ? chain.EnumerateArray().Select(x => x.ToString()).ToArray() : [];
            lines.Add($"进程：{process} {path}\n域名：{host}  {S(m,"destinationIP")}\n命中：{S(c,"rule")} {S(c,"rulePayload")}\n出口：{chains.FirstOrDefault() ?? "未知"}\n链路：{string.Join(" ← ", chains)}");
            if (lines.Count >= 40) break;
        }
        return lines.Count == 0 ? "尚未检测到匹配连接。请让应用发起新请求后重试。\n没有连接记录不代表没有联网，也不能证明规则已经生效。" : string.Join("\n\n", lines);
    }
}
internal sealed class TargetForm : Form
{
    readonly MihomoClient client;
    readonly CancellationTokenSource life = new();
    readonly CancellationToken token;
    bool released;
    static readonly SemaphoreSlim slots = new(2);
    readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Theme.Card };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 45, Text = "优先显示内核已有历史；仅手动测速会联网。策略组延迟取当前选中节点。" };
    public TargetForm(MihomoClient client)
    {
        this.client = client; token = life.Token; Text = "代理目标状态"; Size = new(900, 560); Font = Theme.Body; UiStyle.Window(this); StartPosition = FormStartPosition.CenterParent;
        grid.EnableHeadersVisualStyles = false; grid.BorderStyle = BorderStyle.None; grid.GridColor = Theme.Border;
        grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Card, ForeColor = Theme.Text, SelectionBackColor = Theme.CardHover, SelectionForeColor = Theme.Text, Padding = new Padding(6) };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Card2, ForeColor = Theme.Text };
        grid.RowTemplate.Height = TextRenderer.MeasureText("Ag", Theme.Body).Height + 18;
        grid.Columns.Add("name", "目标"); grid.Columns.Add("status", "实际选择 / 延迟 / 测试时间"); grid.Columns[1].FillWeight = 210;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56 };
        foreach (string label in new[] { "读取已有数据", "测速选中", "全部测速" }) {
            var button = new PillButton { Text = label, AutoSize = true };
            button.Click += async (_, _) => { button.Enabled = false; try { if (label == "读取已有数据") await LoadAsync(); else await TestAsync(label == "全部测速"); } finally { if (!IsDisposed) button.Enabled = true; } }; bar.Controls.Add(button);
        }
        Controls.Add(grid); Controls.Add(bar); Controls.Add(status); Shown += async (_, _) => await LoadAsync();
        KeyPreview = true; KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }
    async Task LoadAsync() { try { var targets = await client.GetTargetsAsync(token); if (IsDisposed) return; grid.Rows.Clear(); foreach (var t in targets) { int index = grid.Rows.Add(t.Name, t.Summary); grid.Rows[index].Tag = t; } } catch (Exception) { if (!IsDisposed) status.Text = "控制器未连接或返回无效数据；未将目标标记为超时。"; } }
    async Task TestAsync(bool all) {
        var rows = (all ? grid.Rows.Cast<DataGridViewRow>() : grid.SelectedRows.Cast<DataGridViewRow>()).ToArray();
        await Task.WhenAll(rows.Select(async row => { try { await slots.WaitAsync(token); } catch (OperationCanceledException) { return; } try { var text = await client.MeasureAsync(row.Cells[0].Value?.ToString() ?? "", token); if (!IsDisposed && row.DataGridView == grid) row.Cells[1].Value = (row.Tag is TargetStatus target && target.Selected.Length > 0 ? "当前：" + target.Selected + " · " : "") + text; } finally { slots.Release(); } }));
    }
    protected override void Dispose(bool disposing) {
        if (disposing && !released) { released = true; life.Cancel(); life.Dispose(); }
        base.Dispose(disposing);
    }

}
