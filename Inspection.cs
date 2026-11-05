using System.Text.Json;
namespace ClashDock;
public sealed record ConnectionRecord(string Id,string Path,string Process,string Host,string Rule,string Payload,string[] Chains)
{
    public string Details => $"进程：{Process}\n路径：{Path}\n域名：{Host}\n命中规则：{Rule} {Payload}\n节点链路（不是公网出口 IP）：{string.Join(" ← ",Chains)}";
    public string Key => JsonSerializer.Serialize(new { Id, Path, Process, Host, Rule, Payload, Chains });
}
public static class ConnectionMatch
{
    public static string PathId(string path) { try { return System.IO.Path.IsPathFullyQualified(path) ? System.IO.Path.GetFullPath(path).TrimEnd('\\','/').ToUpperInvariant() : ""; } catch(ArgumentException) { return ""; } }
    public static string Match(ConnectionRecord c,string target,bool app) {
        if(!app){ try { var host=RoutingCompiler.Domain(c.Host);var domain=RoutingCompiler.Domain(target);return host==domain || host.EndsWith("."+domain,StringComparison.Ordinal) ? "exact":"none"; }catch(ArgumentException){return "none";} }
        string expected=PathId(target),actual=PathId(c.Path);
        if(expected.Length>0 && actual.Length>0)return expected==actual?"exact":"none";
        string name=System.IO.Path.GetFileName(target);
        return name.Equals(c.Process,StringComparison.OrdinalIgnoreCase) || name.Equals(System.IO.Path.GetFileName(c.Path),StringComparison.OrdinalIgnoreCase)?"candidate":"none";
    }
}
public sealed partial class MihomoClient
{
    public async Task<List<ConnectionRecord>> GetConnectionsAsync(CancellationToken ct=default) {
        using var response=await _http.GetAsync(_base+"/connections",HttpCompletionOption.ResponseHeadersRead,ct);await EnsureAsync(response);
        using var source=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();var chunk=new byte[8192];int count;
        while((count=await source.ReadAsync(chunk,ct))>0){if(buffer.Length+count>8*1024*1024)throw new IOException("连接快照过大，停止读取");buffer.Write(chunk,0,count);}
        using var doc=JsonDocument.Parse(buffer.ToArray());var result=new List<ConnectionRecord>();
        foreach(var c in doc.RootElement.GetProperty("connections").EnumerateArray().Take(4096)) {
            if(!c.TryGetProperty("metadata",out var m))continue;
            string S(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?(v.GetString()??"")[..Math.Min(2048,(v.GetString()??"").Length)]:"";
            var chain=c.TryGetProperty("chains",out var cs)&&cs.ValueKind==JsonValueKind.Array?cs.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Take(16).Select(x=>(x.GetString()??"")[..Math.Min(256,(x.GetString()??"").Length)]).ToArray():[];
            result.Add(new(S(c,"id"),S(m,"processPath"),S(m,"process"),S(m,"host"),S(c,"rule"),S(c,"rulePayload"),chain));
        }
        return result;
    }
}
public sealed record Inspection(bool Connected,string Mode,bool? RulesAccepted,string Intent,bool Paused,bool AppliedPaused,long Revision,string Conclusion,string Next,string ProblemCode="unknown",DateTimeOffset? CheckedAt=null);
public sealed class RoutingInspector(MihomoClient client,AutoRouting engine)
{
    public async Task<Inspection> VerifyAsync(string target,bool app,CancellationToken ct) {
        long revision=engine.Revision;var desired=AutoRouting.Copy(engine.Desired);var applied=engine.Applied;
        string intent=app?desired.Applications.FirstOrDefault(e=>ConnectionMatch.PathId(e.Target).Length>0&&ConnectionMatch.PathId(e.Target)==ConnectionMatch.PathId(target))?.Mode??"默认":desired.Websites.FirstOrDefault(e=>e.Target.Equals(target,StringComparison.OrdinalIgnoreCase))?.Mode??"自动";
        try {
            var network=await client.GetNetworkAsync(ct);var rules=await client.GetRuleSequenceAsync(ct);ct.ThrowIfCancellationRequested();
            bool? accepted=RoutingCompiler.RulesConfirmed(applied,rules);
            bool proxyNeeded=RoutingCompiler.Rules(desired).Any(r=>r.EndsWith(","+RoutingCompiler.ProxyGroup,StringComparison.Ordinal));
            bool targetMissing=proxyNeeded && !(await client.GetProxyNamesAsync()).Contains(desired.Proxy);
            bool selectionMismatch=false;
            if(accepted==true) {
                var required=RoutingCompiler.Groups(RoutingCompiler.Rules(applied),applied.Proxy);
                if(required.Count>0) {
                    var groups=await client.GetSelectorGroupsAsync();
                    selectionMismatch=required.Any(g=>!groups.Any(x=>x.Name==(string)g["name"] && x.Now==((string[])g["proxies"])[0]));
                    if(selectionMismatch)accepted=false;
                }
            }
            ct.ThrowIfCancellationRequested();if(revision!=engine.Revision)accepted=null;
            string code=accepted==null?"unknown":targetMissing?"target_missing":selectionMismatch?"target_mismatch":accepted==false?"overwritten":engine.AppliedPaused?"paused":network.Mode!="rule"&&RoutingCompiler.Rules(applied).Count>0?"mode_bypass":"applied";
            string conclusion=code switch {
                "unknown"=>"配置意图在核验期间变化，尚待核验",
                "target_missing"=>"代理目标已失效 · 未自动替换",
                "target_mismatch"=>"托管策略组缺失或选择已变化",
                "overwritten"=>"托管规则缺失、顺序或内容变化 · 未自动恢复",
                "paused"=>"已暂停 · 托管规则已撤下",
                "mode_bypass"=>"规则已加载，但当前模式不按规则分流",
                _=>"规则已加载 · 未验证实际出口"
            };
            string next=code switch {
                "target_missing"=>"在 ClashDock 主界面重新选择代理目标。",
                "mode_bypass"=>"请在 Verge 核对运行模式；本工具不会自动切换。",
                "overwritten" or "target_mismatch"=>"检查 Verge 订阅配置，修复后预览重新应用全部分流。",
                "paused"=>"如需分流，在托盘恢复；观察不会修改网络。",
                "unknown"=>"刷新只读核验。",
                _=>"开始观察后手动刷新网页或操作应用；旧连接可能仍走原路线，不会自动断开连接。"
            };
            return new(true,network.Mode,accepted,intent,engine.Paused,engine.AppliedPaused,revision,conclusion,next,code,DateTimeOffset.UtcNow);
        }catch(OperationCanceledException){throw;}catch(Exception e)when(e is HttpRequestException or IOException or InvalidOperationException or JsonException or TaskCanceledException){return new(false,"未知",null,intent,engine.Paused,engine.AppliedPaused,revision,"控制器未连接或核验失败","请先启动 Verge，检查高级连接配置；核验失败不代表规则生效。","controller_offline",DateTimeOffset.UtcNow);}
    }
}
public sealed class ConnectionObserver(MihomoClient client)
{
    readonly Dictionary<string,(ConnectionRecord Row,string Match)> records=new(StringComparer.Ordinal);
    public IReadOnlyCollection<(ConnectionRecord Row,string Match)> Records=>records.Values.ToArray();
    public void Clear()=>records.Clear();
    public async Task ObserveAsync(string target,bool app,TimeSpan duration,Action changed,CancellationToken ct,int interval=250) {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(duration);
        records.Clear();var watch=System.Diagnostics.Stopwatch.StartNew();
        try { while(watch.Elapsed<duration) {
            var snapshot=await client.GetConnectionsAsync(deadline.Token);deadline.Token.ThrowIfCancellationRequested();
            foreach(var row in snapshot){string match=ConnectionMatch.Match(row,target,app);if(match=="none")continue;if(records.ContainsKey(row.Key))continue;if(records.Count>=128)continue;records[row.Key]=(row,match);}
            deadline.Token.ThrowIfCancellationRequested();changed();
            var remaining=duration-watch.Elapsed;if(remaining<=TimeSpan.Zero)break;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(interval,remaining.TotalMilliseconds)),deadline.Token);
        } } catch(OperationCanceledException) when(!ct.IsCancellationRequested) { }
    }
}
internal sealed class CheckRoutingForm : Form
{
    readonly RoutingInspector inspector;
    readonly ConnectionObserver observer;
    readonly AutoRouting engine;
    readonly string target;readonly bool app;
    readonly CancellationTokenSource life=new();CancellationTokenSource? observing;int generation;bool released;
    readonly Label summary=new(){Dock=DockStyle.Top,AutoSize=true,ForeColor=Theme.Text};
    readonly TextBox details=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,BackColor=Theme.Card,ForeColor=Theme.SubText,BorderStyle=BorderStyle.None,Visible=false};
    readonly PillButton start=new(){Text="开始观察 15 秒",Accent=true,AutoSize=true};
    readonly PillButton stop=new(){Text="停止",Outline=true,AutoSize=true,Enabled=false};
    string configText="正在只读核验…",observed="尚未检测到 · 尚未开始观察";Inspection? last;
    public CheckRoutingForm(MihomoClient client,AutoRouting engine,string target,bool app) {
        this.target=target;this.app=app;this.engine=engine;inspector=new(client,engine);observer=new(client);Text="检查分流 · "+(app?Path.GetFileNameWithoutExtension(target):target);Font=Theme.Body;ClientSize=new(1000,680);MinimumSize=new(840,560);UiStyle.Window(this);Padding=new(24);StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,180));
        var bar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};var verify=new PillButton{Text="重新核验",Outline=true,AutoSize=true};var expand=new CheckBox{Text="进程／域名／规则／链路详情",AutoSize=true,ForeColor=Theme.SubText,Padding=new(6,10,0,0)};
        bar.Controls.AddRange([verify,start,stop,expand]);layout.Controls.Add(bar,0,0);var summaryHost=new Panel{Dock=DockStyle.Fill,AutoScroll=true};summaryHost.Controls.Add(summary);summaryHost.Resize+=(_,_)=>summary.MaximumSize=new Size(Math.Max(100,summaryHost.ClientSize.Width-24),0);layout.Controls.Add(summaryHost,0,1);layout.Controls.Add(details,0,2);layout.RowStyles[2].Height=0;
        expand.CheckedChanged+=(_,_)=>{details.Visible=expand.Checked;layout.RowStyles[2].Height=expand.Checked?180:0;};Controls.Add(layout);
        verify.Click+=async(_,_)=>await VerifyAsync();start.Click+=async(_,_)=>await ObserveAsync();stop.Click+=(_,_)=>StopObservation();Shown+=async(_,_)=>await VerifyAsync();
        KeyPreview=true;KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape)Close();};Render();
    }
    async Task VerifyAsync(){StopObservation();int current=++generation;try{var result=await inspector.VerifyAsync(target,app,life.Token);if(released||current!=generation)return;last=result;var view=RoutingState.Describe(engine,result);configText=$"{view.Message}\n下一步：{view.Next}\n\n控制器：{(result.Connected?"已连接":"未连接／核验失败")}    运行模式：{result.Mode}\n当前意图：{result.Intent}    暂停意图：{(result.Paused?"是":"否")}    已确认暂停：{(result.AppliedPaused?"是":"否")}\n托管规则：{(result.RulesAccepted==true?"已加载（未验证实际出口）":result.RulesAccepted==false?"未通过核验":"尚待核验")}\n最近核验：{result.CheckedAt?.ToLocalTime():HH:mm:ss}";Render();}catch(OperationCanceledException){}}
    async Task ObserveAsync(){StopObservation();observing=CancellationTokenSource.CreateLinkedTokenSource(life.Token);var request=observing;int current=++generation;start.Enabled=false;stop.Enabled=true;observed="正在观察 15 秒 · 请刷新网页或操作应用";Render();
        try{await observer.ObserveAsync(target,app,TimeSpan.FromSeconds(15),()=>{if(!released&&current==generation){observed=$"正在观察 · 精确匹配 {observer.Records.Count(r=>r.Match=="exact")} 条；名称候选 {observer.Records.Count(r=>r.Match=="candidate")} 条";Render();}},request.Token);if(!released&&current==generation){observed=observer.Records.Count==0?"尚未检测到匹配连接（不等于未联网或已生效）":$"已观察到：精确匹配 {observer.Records.Count(r=>r.Match=="exact")} 条；名称候选 {observer.Records.Count(r=>r.Match=="candidate")} 条（未精确确认）";Render();}}
        catch(OperationCanceledException){}catch(Exception){if(!released&&current==generation){observed="观察中断：连接读取失败，保留本次已观察内容。";Render();}}
        finally{if(ReferenceEquals(observing,request)){observing=null;request.Dispose();if(!released){start.Enabled=true;stop.Enabled=false;}}else request.Dispose();}
    }
    void StopObservation(){generation++;observing?.Cancel();observing=null;if(!released){start.Enabled=true;stop.Enabled=false;observed=observer.Records.Count==0?"尚未检测到 · 已停止观察":$"已停止 · 保留 {observer.Records.Count} 条本次记录";Render();}}
    void Render(){if(released)return;summary.Text=configText+"\n\n"+observed+"\n\n连接采样有时间间隔，极短连接可能错过。链路名称不是公网出口 IP；不会访问外部 IP 查询服务。";details.Text=string.Join("\n\n",observer.Records.Select(r=>(r.Match=="candidate"?"名称候选 · 未精确确认\n":"精确匹配\n")+r.Row.Details)).Replace("\n","\r\n");}
    protected override void Dispose(bool disposing){if(disposing&&!released){released=true;generation++;observing?.Cancel();life.Cancel();observer.Clear();details.Text="";life.Dispose();}base.Dispose(disposing);}
}
