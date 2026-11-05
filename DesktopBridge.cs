using System.IO.Pipes;
using System.Text.Json;
namespace ClashDock;
public sealed class BridgeDispatcher : IDisposable
{
    readonly CancellationTokenSource life=new(); bool disposed;
    readonly MihomoClient client;readonly AutoRouting engine;readonly Action<string> openCheck;
    readonly Dictionary<string,(string Fingerprint,BridgeReply Reply,long Time)> seen=new(StringComparer.Ordinal);
    readonly Dictionary<string,(long Sequence,long Time)> sequences=new(StringComparer.Ordinal);
    readonly RoutingInspector inspector;Inspection? inspection;DateTimeOffset checkedAt=DateTimeOffset.MinValue;Task? verifying;long verifiedRevision=-1;DateTimeOffset targetsAt=DateTimeOffset.MinValue;
    public string Session { get; }=Guid.NewGuid().ToString("N");
    public BridgeDispatcher(MihomoClient client,AutoRouting engine,Action<string> openCheck){this.client=client;this.engine=engine;this.openCheck=openCheck;inspector=new(client,engine);}
    public async Task<BridgeReply> HandleAsync(BridgeEnvelope envelope,CancellationToken ct)
    {
        var r=envelope.Request;if(disposed)return BridgeProtocol.Error(r.Id,"desktop_offline");using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,life.Token);ct=linked.Token;long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if(envelope.Origin!=BridgeProtocol.Origin)return BridgeProtocol.Error(r.Id,"unauthorized_origin");
        var error=BridgeProtocol.Validate(r,now);if(error!=null)return BridgeProtocol.Error(r.Id,error);
        string domain="";if(r.Op is not("templates" or "start")){try{domain=RoutingCompiler.Domain(r.Domain);}catch(ArgumentException){return BridgeProtocol.Error(r.Id,"invalid_domain");}}
        ct.ThrowIfCancellationRequested();if(r.Op=="start")return BridgeProtocol.Error(r.Id,"unknown_operation");
        if(r.Op=="templates")return new(1,r.Id,true,"ok","",JsonSerializer.SerializeToElement(WebsiteTemplates.All,BridgeProtocol.Json));
        if(r.Op=="state") {
            if(DateTimeOffset.UtcNow-targetsAt>TimeSpan.FromSeconds(30)){targetsAt=DateTimeOffset.UtcNow;await client.Targets.RefreshAsync(ct);}
            if(verifying==null && (verifiedRevision!=engine.Revision || DateTimeOffset.UtcNow-checkedAt>TimeSpan.FromSeconds(4))) verifying=Verify(domain);
            if(verifying is Task flight){await flight.WaitAsync(ct);if(ReferenceEquals(verifying,flight)&&flight.IsCompleted)verifying=null;}
            ct.ThrowIfCancellationRequested();if(r.ExpiresAt<DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())return BridgeProtocol.Error(r.Id,"expired");
            return State(r.Id,domain);
        }
        if(r.Session!=Session)return WithState(r.Id,"session_changed",domain);
        foreach(var key in seen.Where(x=>now-x.Value.Time>300000).Select(x=>x.Key).ToArray())seen.Remove(key);
        foreach(var key in sequences.Where(x=>now-x.Value.Time>300000).Select(x=>x.Key).ToArray())sequences.Remove(key);
        string keyId=r.Client+":"+r.Id;string fingerprint=JsonSerializer.Serialize(r,BridgeProtocol.Json);
        if(seen.TryGetValue(keyId,out var previous))return previous.Fingerprint==fingerprint?previous.Reply:BridgeProtocol.Error(r.Id,"duplicate_conflict");
        if(BridgeProtocol.Mutation(r.Op)){
            if(sequences.TryGetValue(r.Client,out var last)&&r.Sequence<=last.Sequence)return WithState(r.Id,"stale_sequence",domain);
            if(r.BaseRevision!=engine.Revision)return WithState(r.Id,"conflict",domain);
        }
        ct.ThrowIfCancellationRequested();if(r.ExpiresAt<DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())return BridgeProtocol.Error(r.Id,"expired");
        BridgeReply reply;
        try{
            if(r.Op=="check"){openCheck(domain);reply=new(1,r.Id,true,"opened","已打开只读检查面板，不代表规则生效。",BuildState(domain));}
            else if(r.Op=="retry"){engine.Retry();reply=State(r.Id,domain,"accepted");}
            else {
                var draft=AutoRouting.Copy(engine.Desired);var domains=r.Op=="setSite"?new[]{domain}:TemplateDomains(r);
                foreach(var host in domains){draft.Websites.RemoveAll(e=>e.Target.Equals(host,StringComparison.OrdinalIgnoreCase));draft.Websites.Add(new(){Target=host,Mode=r.Mode});}
                RoutingCompiler.Rules(draft);engine.Update(draft);reply=State(r.Id,domain,"accepted");
            }
        }catch(ArgumentException){reply=BridgeProtocol.Error(r.Id,"invalid_parameter");}catch(InvalidOperationException){reply=BridgeProtocol.Error(r.Id,"invalid_parameter");}
        if(BridgeProtocol.Mutation(r.Op))sequences[r.Client]=(r.Sequence,now);
        if(sequences.Count>128)sequences.Remove(sequences.MinBy(x=>x.Value.Time).Key);
        seen[keyId]=(fingerprint,reply,now);if(seen.Count>256)seen.Remove(seen.MinBy(x=>x.Value.Time).Key);
        return reply;
    }
    static string[] TemplateDomains(BridgeRequest r){var template=WebsiteTemplates.All.FirstOrDefault(t=>t.Id==r.Template)??throw new ArgumentException();if(r.Domains.Length==0)throw new ArgumentException();var domains=r.Domains.Select(RoutingCompiler.Domain).Distinct().ToArray();if(domains.Any(d=>!template.Domains.Contains(d,StringComparer.Ordinal)))throw new ArgumentException();return domains;}
    async Task Verify(string domain){try{var result=await inspector.VerifyAsync(domain,false,life.Token);if(disposed)return;inspection=result;verifiedRevision=inspection.Revision;checkedAt=DateTimeOffset.UtcNow;}catch(Exception){if(disposed)return;inspection=null;checkedAt=DateTimeOffset.UtcNow;}}
    public void Dispose(){if(disposed)return;disposed=true;life.Cancel();seen.Clear();sequences.Clear();life.Dispose();}
    BridgeReply State(string id,string domain,string code="ok")=>new(1,id,true,code,"",BuildState(domain));
    BridgeReply WithState(string id,string code,string domain)=>BridgeProtocol.Error(id,code) with{Data=BuildState(domain)};
    JsonElement BuildState(string domain){
        var desired=engine.Desired;var entry=desired.Websites.FirstOrDefault(e=>e.Target.Equals(domain,StringComparison.OrdinalIgnoreCase));
        var inherited=desired.Websites.Where(e=>e.Mode!="自动"&&domain.EndsWith("."+e.Target,StringComparison.OrdinalIgnoreCase)).OrderByDescending(e=>e.Target.Length).FirstOrDefault();
        string state=engine.StateCode;
        if(inspection?.Connected==false)state="controller_offline";
        else if(state is "applied" or "paused" && (inspection==null||inspection.Revision!=engine.Revision))state="unknown";
        else if(inspection?.RulesAccepted==false && state is "applied" or "paused" or "unknown")state="overwritten";
        return JsonSerializer.SerializeToElement(new {session=Session,revision=engine.Revision,domain,mode=entry?.Mode??"自动",inherited=entry?.Mode is null or "自动"?inherited?.Target:null,inheritedMode=inherited?.Mode,
            paused=engine.Paused,appliedPaused=engine.AppliedPaused,state,controllerConnected=inspection?.Connected,runningMode=inspection?.Mode??"未知",rulesAccepted=inspection?.RulesAccepted,verifiedAt=inspection==null?(DateTimeOffset?)null:checkedAt,
            target=desired.Proxy,targetDisplay=client.Targets.Display(desired.Proxy),targetTime=client.Targets.Find(desired.Proxy)?.Time,
            scope="当前域名及其子域名；同域名标签页共用，也影响其他应用访问此域名",generation="1.2.0"},BridgeProtocol.Json);
    }
}
public sealed class DesktopBridge : IDisposable
{
    readonly Control dispatcher;readonly BridgeDispatcher handler;readonly CancellationTokenSource life=new();readonly SemaphoreSlim slots=new(4);readonly string pipe;
    bool stopped;readonly HashSet<Task> active=new();readonly Task listener;
    public DesktopBridge(Control dispatcher,BridgeDispatcher handler,string? directory=null){this.dispatcher=dispatcher;this.handler=handler;pipe=BridgeProtocol.PipeName(directory??AppContext.BaseDirectory);listener=Listen();}
    async Task Listen(){while(!life.IsCancellationRequested){bool acquired=false;NamedPipeServerStream? server=null;try{await slots.WaitAsync(life.Token);acquired=true;server=new(pipe,PipeDirection.InOut,4,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);await server.WaitForConnectionAsync(life.Token);Track(Serve(server));server=null;acquired=false;}catch(OperationCanceledException){break;}catch(IOException){if(!life.IsCancellationRequested)await Task.Delay(200);}finally{server?.Dispose();if(acquired)slots.Release();}}}
    async Task Serve(NamedPipeServerStream pipe){using(pipe)using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(life.Token)){timeout.CancelAfter(TimeSpan.FromSeconds(18));try{
        var bytes=await BridgeProtocol.ReadAsync(pipe,timeout.Token);if(bytes==null)return;BridgeReply reply;
        try{var envelope=JsonSerializer.Deserialize<BridgeEnvelope>(bytes,BridgeProtocol.Json);if(envelope?.Request==null)throw new JsonException();
            using var monitor=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var disconnected=pipe.ReadAsync(new byte[1],monitor.Token).AsTask();var dispatch=Dispatch(envelope,timeout.Token);
            if(await Task.WhenAny(dispatch,disconnected)==disconnected){timeout.Cancel();try{await dispatch;}catch(OperationCanceledException){}return;}
            reply=await dispatch;monitor.Cancel();try{await disconnected;}catch(OperationCanceledException){}
}catch(JsonException){reply=BridgeProtocol.Error("","invalid_message");}
        await BridgeProtocol.WriteAsync(pipe,JsonSerializer.SerializeToUtf8Bytes(reply,BridgeProtocol.Json),timeout.Token);
    }catch(Exception e)when(e is IOException or OperationCanceledException or InvalidOperationException){}finally{slots.Release();}}}
    Task<BridgeReply> Dispatch(BridgeEnvelope envelope,CancellationToken ct){var result=new TaskCompletionSource<BridgeReply>(TaskCreationOptions.RunContinuationsAsynchronously);var registration=ct.Register(()=>result.TrySetCanceled(ct));
        try{dispatcher.BeginInvoke((Action)(async()=>{try{ct.ThrowIfCancellationRequested();if(stopped)throw new OperationCanceledException(ct);result.TrySetResult(await handler.HandleAsync(envelope,ct));}catch(OperationCanceledException){result.TrySetCanceled(ct);}catch(Exception){result.TrySetResult(BridgeProtocol.Error(envelope.Request.Id,"request_failed"));}finally{registration.Dispose();}}));}catch(InvalidOperationException){registration.Dispose();result.TrySetResult(BridgeProtocol.Error(envelope.Request.Id,"desktop_offline"));}return result.Task;}
    void Track(Task task){lock(active)active.Add(task);_=task.ContinueWith(t=>{lock(active)active.Remove(t);},TaskScheduler.Default);}
    public async Task StopAsync(){if(stopped)return;stopped=true;life.Cancel();handler.Dispose();await listener;Task[] tasks;lock(active)tasks=active.ToArray();await Task.WhenAll(tasks);life.Dispose();slots.Dispose();}
    public void Dispose(){_=StopAsync();}
}
