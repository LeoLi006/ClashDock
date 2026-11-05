using System.Net;
using System.Text.Json;
namespace ClashDock;
public sealed record TargetStatus(string Name, string Selected, int? Delay, DateTimeOffset? Time,
    string State = "history", bool IsGroup = false, bool SelectionKnown = true, string Leaf = "")
{
    public string Latency => State switch { "testing" => "测速中", "offline" => "控制器连接失败", "failed" => "测速失败", "timeout" => "超时", _ => Delay == null ? "未测速" : Delay == 0 ? "超时" : Delay + " ms" };
    public string Summary => (IsGroup ? SelectionKnown ? "当前节点：" + Selected + " · " : "选择路径未知 · " : "") + Latency + (Time == null ? " · 时间未知" : " · " + Time.Value.ToLocalTime().ToString("MM-dd HH:mm:ss") + (DateTimeOffset.Now - Time > TimeSpan.FromMinutes(10) ? "（陈旧）" : ""));
    public string Display => Name + (IsGroup ? SelectionKnown ? " → " + Selected : " → 未知" : "") + " · " + Latency + (Time != null ? " · " + Time.Value.ToLocalTime().ToString("HH:mm") + (DateTimeOffset.Now - Time > TimeSpan.FromMinutes(10) ? " 陈旧" : "") : "");
}
public sealed record LatencyResult(string State, int? Delay, DateTimeOffset? Time)
{
    public string Text => State switch { "cancelled" => "已取消", "offline" => "控制器连接失败（不是节点超时）", "timeout" => "节点测速超时", "failed" => "测速失败", _ => Delay + " ms" };
}
public sealed partial class MihomoClient
{
    static readonly SemaphoreSlim MeasurementSlots = new(2);
    public async Task<List<TargetStatus>> GetTargetsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(_base + "/proxies", ct); await EnsureAsync(response);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var map = doc.RootElement.GetProperty("proxies"); var result = new List<TargetStatus>();
        foreach (var p in map.EnumerateObject().Take(4096))
        {
            if (p.Name == "GLOBAL" || p.Name.StartsWith("__ClashDock_") || p.Value.GetProperty("type").GetString() is "Direct" or "Reject" or "Pass" or "Compatible") continue;
            var v = p.Value; var seen = new HashSet<string>(StringComparer.Ordinal) { p.Name }; var chain = new List<string>(); string leafName = p.Name;
            bool group = v.TryGetProperty("all", out _) || v.TryGetProperty("now", out _), known = true;
            while (v.TryGetProperty("now", out var next)) {
                if (next.ValueKind != JsonValueKind.String || next.GetString() is not string name || !seen.Add(name) || !map.TryGetProperty(name, out var child) || chain.Count >= 16) { known = false; break; }
                chain.Add(name); leafName = name; v = child;
            }
            if (group && (chain.Count == 0 || v.TryGetProperty("all", out _))) known = false;
            int? delay = null; DateTimeOffset? time = null;
            if ((!group || known) && v.TryGetProperty("history", out var history) && history.ValueKind == JsonValueKind.Array && history.GetArrayLength() > 0) {
                var last = history[history.GetArrayLength()-1];
                if (last.TryGetProperty("delay", out var d) && d.TryGetInt32(out var n) && n >= 0) delay = n;
                if (last.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(t.GetString(), out var parsed)) time = parsed;
            }
            result.Add(new(p.Name, string.Join(" → ",chain), delay, time, "history", group, known, known ? leafName : ""));
        }
        return result.OrderBy(x => x.Name,StringComparer.OrdinalIgnoreCase).ToList();
    }
    public async Task<LatencyResult> MeasureTargetAsync(string name, CancellationToken ct)
    {
        bool entered = false;
        try {
            await MeasurementSlots.WaitAsync(ct); entered = true;
            using var response = await _http.GetAsync(_base + "/proxies/" + Uri.EscapeDataString(name) + "/delay?timeout=5000&url=" + Uri.EscapeDataString("https://www.gstatic.com/generate_204"), ct);
            if (response.StatusCode == HttpStatusCode.GatewayTimeout) return new("timeout",0,DateTimeOffset.UtcNow);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return new("offline",null,null);
            if (!response.IsSuccessStatusCode) return new("failed",null,DateTimeOffset.UtcNow);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            int delay = doc.RootElement.GetProperty("delay").GetInt32();
            return delay < 0 ? new("failed",null,DateTimeOffset.UtcNow) : new(delay == 0 ? "timeout" : "measured",delay,DateTimeOffset.UtcNow);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new("cancelled",null,null); }
        catch (HttpRequestException) { return new("offline",null,null); }
        catch (Exception e) when (e is InvalidOperationException or JsonException or TaskCanceledException or KeyNotFoundException or FormatException) { return new("failed",null,DateTimeOffset.UtcNow); }
        finally { if (entered) MeasurementSlots.Release(); }
    }
    public async Task<string> MeasureAsync(string name, CancellationToken ct) => (await MeasureTargetAsync(name,ct)).Text;
    // Compatibility API; the UI now uses the bounded observation panel.
    public async Task<string> DiagnoseAsync(string target, bool app) {
        var rows = (await GetConnectionsAsync()).Where(c => ConnectionMatch.Match(c,target,app) != "none").Take(40).ToArray();
        return rows.Length == 0 ? "尚未检测到匹配连接；不能证明未联网或规则已生效。" : string.Join("\n\n",rows.Select(c=>c.Details));
    }
}

public sealed class TargetCache : IDisposable
{
    readonly MihomoClient client;
    readonly object gate = new();
    Dictionary<string,TargetStatus> items = new(StringComparer.Ordinal);
    readonly CancellationTokenSource life = new();
    long readGeneration, measureGeneration;
    bool disposed;
    public string ReadState { get; private set; } = "unknown";
    public event Action? Changed;
    public TargetCache(MihomoClient client) => this.client = client;
    public TargetStatus[] Snapshot { get { lock(gate) return items.Values.OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToArray(); } }
    public TargetStatus? Find(string name) { lock(gate) return items.GetValueOrDefault(name); }
    public string Display(string name) => (Find(name)?.Display ?? name + (ReadState == "ready" ? " · 目标失效" : " · 状态未知")) + (ReadState == "offline" ? " · 控制器未连接（历史）" : "");
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct,life.Token); long generation = Interlocked.Increment(ref readGeneration); long measurements = Interlocked.Read(ref measureGeneration);
        try {
            var fetched = await client.GetTargetsAsync(cancel.Token); cancel.Token.ThrowIfCancellationRequested();
            lock(gate) {
                if (disposed || generation != readGeneration) return;
                var next = fetched.ToDictionary(x=>x.Name,StringComparer.Ordinal);
                foreach (var pair in next.ToArray()) if (items.TryGetValue(pair.Key,out var old) && old.Selected == pair.Value.Selected && (measurements != measureGeneration || old.Time > pair.Value.Time || old.Time != null && pair.Value.Time == null) && old.State != "history") next[pair.Key] = old;
                items = next; ReadState = "ready";
            }
            Changed?.Invoke();
        } catch (OperationCanceledException) { }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException or JsonException or TaskCanceledException) {
            if (!cancel.IsCancellationRequested && !disposed && generation == readGeneration) { ReadState = "offline"; Changed?.Invoke(); }
        }
    }
    public async Task MeasureAsync(string name, CancellationToken ct)
    {
        var before = Find(name); if (before == null) return;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct,life.Token);
        // A group result without a post-test selection read cannot safely be attributed to its former node.
        var probeName = before.IsGroup && before.SelectionKnown ? before.Leaf : name;
        var result = before.IsGroup && !before.SelectionKnown ? new LatencyResult("failed",null,null) : await client.MeasureTargetAsync(probeName,cancel.Token);
        if (cancel.IsCancellationRequested || result.State == "cancelled") return;
        string selected = before.Selected; bool known = before.SelectionKnown;
        if (before.IsGroup && result.State is "measured" or "timeout") {
            try { var after = (await client.GetTargetsAsync(cancel.Token)).FirstOrDefault(t=>t.Name==name); known = after != null && after.SelectionKnown && after.Selected == selected; }
            catch (Exception) when (!cancel.IsCancellationRequested) { known = false; }
        }
        lock(gate) {
            if (disposed || cancel.IsCancellationRequested || !items.TryGetValue(name,out var current) || current.Selected != selected) return;
            Interlocked.Increment(ref measureGeneration);
            items[name] = before with { State = known || !before.IsGroup ? result.State : "failed", Delay = known || !before.IsGroup ? result.Delay : null, Time = result.Time, SelectionKnown = known };
        }
        Changed?.Invoke();
    }
    public Task MeasureAllAsync(CancellationToken ct) => Task.WhenAll(Snapshot.Select(t=>MeasureAsync(t.Name,ct)));
    public void Dispose() { if (disposed) return; disposed=true; life.Cancel(); Changed=null; lock(gate)items.Clear(); life.Dispose(); }
}

internal sealed class TargetForm : Form
{
    readonly TargetCache cache;
    readonly CancellationTokenSource life = new();
    readonly CancellationToken token;
    bool released;
    readonly DataGridView grid = new() { Dock=DockStyle.Fill, ReadOnly=true, AllowUserToAddRows=false, RowHeadersVisible=false, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill, SelectionMode=DataGridViewSelectionMode.FullRowSelect, BackgroundColor=Theme.Card };
    readonly Label status = new() { Dock=DockStyle.Bottom, Height=64, Text="组的延迟仅代表当前选中末端节点；无法确认路径时显示未知。仅手动测速联网。" };
    public TargetForm(MihomoClient client) {
        cache=client.Targets; token=life.Token; Text="代理目标状态"; Size=new(1000,620); Font=Theme.Body; UiStyle.Window(this); StartPosition=FormStartPosition.CenterParent;
        grid.EnableHeadersVisualStyles=false; grid.BorderStyle=BorderStyle.None; grid.GridColor=Theme.Border;
        grid.DefaultCellStyle=new() { BackColor=Theme.Card,ForeColor=Theme.Text,SelectionBackColor=Theme.CardHover,SelectionForeColor=Theme.Text,Padding=new(6) };
        grid.ColumnHeadersDefaultCellStyle=new() { BackColor=Theme.Card2,ForeColor=Theme.Text }; grid.RowTemplate.Height=TextRenderer.MeasureText("Ag",Theme.Body).Height+18;
        grid.Columns.Add("name","目标"); grid.Columns.Add("status","实际选择 / 延迟 / 测试时间"); grid.Columns[1].FillWeight=220;
        var bar=new FlowLayoutPanel { Dock=DockStyle.Top,AutoSize=true };
        foreach (var label in new[]{"读取已有数据","测速选中","全部测速"}) { var b=new PillButton { Text=label,AutoSize=true }; b.Click+=async(_,_)=>{ b.Enabled=false; try { if(label=="读取已有数据") await LoadAsync(); else await TestAsync(label=="全部测速"); } catch(OperationCanceledException) {} finally { if(!IsDisposed)b.Enabled=true; } };bar.Controls.Add(b); }
        Controls.Add(grid);Controls.Add(bar);Controls.Add(status);cache.Changed+=CacheChanged;Shown+=async(_,_)=>await LoadAsync();KeyPreview=true;KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape)Close();};
    }
    void CacheChanged() { if(IsDisposed || released)return; if(InvokeRequired){try{BeginInvoke((Action)Render);}catch(InvalidOperationException){} }else Render(); }
    void Render() { if(IsDisposed || released)return; var selected=grid.SelectedRows.Cast<DataGridViewRow>().Select(r=>r.Cells[0].Value?.ToString()).ToHashSet(); grid.Rows.Clear();foreach(var t in cache.Snapshot){int i=grid.Rows.Add(t.Name,t.Summary);grid.Rows[i].Selected=selected.Contains(t.Name);}status.Text=cache.ReadState=="offline"?"控制器连接失败；保留旧记录，请留意测试时间。":"组的延迟仅代表当前选中末端节点；无法确认路径时显示未知。仅手动测速联网。"; }
    async Task LoadAsync(){await cache.RefreshAsync(token);if(!released)Render();}
    async Task TestAsync(bool all){ if(all)await cache.MeasureAllAsync(token);else await Task.WhenAll(grid.SelectedRows.Cast<DataGridViewRow>().Select(r=>cache.MeasureAsync(r.Cells[0].Value?.ToString()??"",token)).ToArray());}
    protected override void Dispose(bool disposing){if(disposing && !released){released=true;cache.Changed-=CacheChanged;life.Cancel();life.Dispose();}base.Dispose(disposing);}
}
