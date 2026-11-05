using ClashDock;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
internal static class EnhancedChecks
{
    static void Wait(Task task){var until=Environment.TickCount64+20000;while(!task.IsCompleted&&Environment.TickCount64<until){Application.DoEvents();Thread.Sleep(3);}if(!task.IsCompleted)throw new Exception("enhanced test timeout");task.GetAwaiter().GetResult();}
    static T Result<T>(Task<T> task){Wait(task);return task.Result;}
    static T Field<T>(object x,string name)=>(T)x.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(x)!;
    static object Empty()=>new{connections=Array.Empty<object>()};
    static object Connection(string host="a.example.com",string path=@"C:\Apps\A.exe",string process="A.exe",string id="one")=>new{connections=new[]{new{id,metadata=new{host,processPath=path,process},rule="DomainSuffix",rulePayload="example.com",chains=new[]{"node","group"}}}};
    public static void Run(Action<bool,string> check,string temp)
    {
        var fake=new FeatureChecks.Fake();using var client=new MihomoClient("127.0.0.1",1,"",fake);var cache=client.Targets;
        Wait(cache.RefreshAsync());check(cache.Find("node")!.Display.Contains("55 ms")&&cache.Find("node")!.Summary.Contains("陈旧"),"Cache preserves latency and stale test time");
        check(cache.Find("other")!.Display.Contains("未测速")&&cache.Find("timeout")!.Display.Contains("超时"),"Dropdown cache distinguishes untested from real timeout");
        var proxy=new ModernSelect{DisplayText=cache.Display};proxy.SetItems(cache.Snapshot.Select(t=>t.Name),"node");int changes=0;proxy.SelectedIndexChanged+=(_,_)=>changes++;
        Wait(cache.MeasureAsync("node",CancellationToken.None));proxy.SetItems(cache.Snapshot.Select(t=>t.Name).Reverse(),"node");proxy.RefreshDisplay();
        check(proxy.SelectedItem=="node"&&proxy.FormatItem("node").Contains("42 ms")&&changes==0,"Refresh and latency display keep identity and selection event silent");
        fake.Requests.Clear();Wait(cache.MeasureAsync("group",CancellationToken.None));
        check(cache.Find("group")!.Selected=="node"&&fake.Requests.Contains("GET /proxies/node/delay")&&!fake.Requests.Any(x=>x.StartsWith("PUT")||x.StartsWith("PATCH")),"Group probe measures resolved leaf without selecting policy or applying rules");
        using(var cancelled=new CancellationTokenSource()){var before=cache.Find("other");var measuring=cache.MeasureAsync("other",cancelled.Token);cancelled.Cancel();Wait(measuring);check(cache.Find("other")==before,"Cancelled probe cannot overwrite cached status");}
        fake.ProxyJson="{\"proxies\":{\"bad\":{\"type\":\"Selector\",\"now\":\"missing\",\"all\":[\"missing\"]},\"node\":{\"type\":\"Shadowsocks\",\"history\":[]}}}";
        Wait(cache.RefreshAsync());check(!cache.Find("bad")!.SelectionKnown&&cache.Find("bad")!.Display.Contains("未知"),"Unresolvable group path stays unknown");
        check(cache.Find("group")==null&&cache.Display("group").Contains("目标失效"),"Removed subscription target remains identifiable as invalid");fake.ProxyJson="{\"proxies\":{\"组\":{\"type\":\"Selector\",\"now\":\"香港 → A\",\"all\":[\"香港 → A\"]},\"香港 → A\":{\"type\":\"Shadowsocks\",\"history\":[]}}}";Wait(cache.RefreshAsync());check(cache.Find("组")!.Leaf=="香港 → A","Selected leaf identity is independent of path display separators");fake.ProxyJson=null;Wait(cache.RefreshAsync());
        fake.MaxActive=0;Wait(Task.WhenAll(cache.MeasureAllAsync(CancellationToken.None),cache.MeasureAllAsync(CancellationToken.None)));check(fake.MaxActive<=2,"Shared cache probes have global concurrency cap across callers");
        fake.Offline=true;Wait(cache.RefreshAsync());check(cache.ReadState=="offline"&&cache.Find("node")!=null,"Controller read failure retains old timestamped cache");fake.Offline=false;
        var exact=new ConnectionRecord("1",@"C:\Apps\A.exe","A.exe","video.example.com","DomainSuffix","example.com",["node"]);
        check(ConnectionMatch.Match(exact,"example.com",false)=="exact"&&ConnectionMatch.Match(exact with{Host="notexample.com"},"example.com",false)=="none","Website observation uses DNS boundary, not substring");
        check(ConnectionMatch.Match(exact,@"c:\apps\a.exe",true)=="exact"&&ConnectionMatch.Match(exact with{Path=@"C:\Other\A.exe"},@"C:\Apps\A.exe",true)=="none","Application matching prefers normalized full path");
        check(ConnectionMatch.Match(exact with{Path=""},@"C:\Apps\A.exe",true)=="candidate","Missing path produces explicitly unconfirmed name candidate");
        fake.ConnectionReads=0;fake.Connections=n=>n==2?Connection():Empty();var observer=new ConnectionObserver(client);int callbacks=0;
        Wait(observer.ObserveAsync("example.com",false,TimeSpan.FromMilliseconds(70),()=>callbacks++,CancellationToken.None,5));
        check(observer.Records.Count==1&&callbacks>2,"Observation captures and retains transient connection after it disappears");
        fake.Connections=n=>Connection();Wait(observer.ObserveAsync("example.com",false,TimeSpan.FromMilliseconds(45),()=>{},CancellationToken.None,5));check(observer.Records.Count==1,"Observation deduplicates repeated connection snapshots");
        fake.Connections=n=>Empty();Wait(observer.ObserveAsync("example.com",false,TimeSpan.FromMilliseconds(25),()=>{},CancellationToken.None,5));check(observer.Records.Count==0,"No connection remains no observation, not network verdict");
        using(var cancel=new CancellationTokenSource()){int called=0;var observing=observer.ObserveAsync("example.com",false,TimeSpan.FromSeconds(15),()=>called++,cancel.Token,5);cancel.Cancel();try{Wait(observing);}catch(OperationCanceledException){}int old=called;Application.DoEvents();check(observing.IsCompleted&&called==old,"Stop cancels observation and prevents later callbacks");}
        fake.Connections=n=>new{connections=Enumerable.Range(0,200).Select(i=>new{id=i.ToString(),metadata=new{host="a.example.com",processPath=@"C:\Apps\A.exe",process="A.exe"},rule="DomainSuffix",rulePayload="example.com",chains=new[]{"node"}})};
        Wait(observer.ObserveAsync("example.com",false,TimeSpan.FromMilliseconds(100),()=>{},CancellationToken.None,10));check(observer.Records.Count==128,"Observation record storage has a hard 128-row cap");
        fake.ConnectionDelayMs=2000;var elapsed=Stopwatch.StartNew();Wait(observer.ObserveAsync("example.com",false,TimeSpan.FromMilliseconds(40),()=>{},CancellationToken.None,5));check(elapsed.ElapsedMilliseconds<500,"Observation deadline cancels slow controller read");fake.ConnectionDelayMs=0;fake.Connections=n=>Empty();
        var legacy=JsonSerializer.Deserialize<Settings>("{\"ForceRuleModeOnStart\":true,\"Presets\":[{}],\"Host\":\"127.0.0.1\",\"Port\":1234,\"Secret\":\"fixture\",\"Routing\":{\"Applications\":[{\"Target\":\"legacy.exe\",\"Mode\":\"自动\"}]}}");check(legacy!.Port==1234&&legacy.Secret=="fixture"&&legacy.Routing.Applications.Count==1,"Legacy settings remain compatible without losing connection or app entries");
        var pinsFile=Path.Combine(temp,"pins.json");var pins=new PinStore(pinsFile);pins.Toggle(@"C:\Apps\B.exe");check(new PinStore(pinsFile).IsPinned(@"c:\apps\b.exe"),"Pins persist by normalized path across restart");
        string root=Path.Combine(temp,"enhanced-verge");Directory.CreateDirectory(Path.Combine(root,"profiles"));File.WriteAllText(Path.Combine(root,"profiles.yaml"),"items:\n- uid: Script\n  file: Script.js\n");File.WriteAllText(Path.Combine(root,"profiles","Script.js"),"function main(c){return c;}");File.WriteAllText(Path.Combine(root,"clash-verge.yaml"),FeatureChecks.Fake.Source);
        var settings=new Settings{Routing=new(){VergeDirectory=root,Proxy="group"}};var engine=new AutoRouting(client,settings,Path.Combine(temp,"enhanced-intent.json"),()=>{});
        using(var form=new RoutingForm(client,settings,true,engine,pins)){
            void Merge(DiscoveredApp[] apps) => typeof(RoutingForm).GetMethod("MergeApps", BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,new object[]{apps});
            form.Show();Application.DoEvents();Merge([new("Alpha",@"C:\Apps\A.exe","test"),new("Zulu",@"C:\Apps\B.exe","test")]);var apps=Field<DataGridView>(form,"_apps");
            check(apps.Rows[0].Cells[0].Value?.ToString()==@"C:\Apps\B.exe","Pinned app sorts before friendly-name order");
            Field<TextBox>(form,"_appSearch").Text="Alpha";check(apps.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible).Count()==1&&apps.Rows.Cast<DataGridViewRow>().First(r=>r.Visible).Cells[0].Value?.ToString()==@"C:\Apps\A.exe","Search hides pinned apps that do not match");
            int writes=fake.Writes;typeof(RoutingForm).GetMethod("TogglePin",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,new object[]{@"C:\Apps\A.exe"});check(fake.Writes==writes&&engine.Revision==0,"Pin only saves preferences, never queues network mutation");
            Merge([new("Alpha",@"C:\Apps\A.exe","rescan"),new("Zulu",@"C:\Apps\B.exe","rescan")]);check(pins.IsPinned(@"C:\Apps\A.exe"),"Rescan preserves pin preferences");
            var dispatcher=new BridgeDispatcher(client,engine,_=>{});long seq=0;string caller=Guid.NewGuid().ToString("N");
            BridgeRequest Request(string op,string mode="自动",string domain="video.example.com")=>new(){Version=1,Id=Guid.NewGuid().ToString("N"),Client=caller,Op=op,Domain=domain,Mode=mode,Session=dispatcher.Session,Sequence=++seq,BaseRevision=engine.Revision,ExpiresAt=DateTimeOffset.UtcNow.AddSeconds(15).ToUnixTimeMilliseconds()};
            BridgeReply Send(BridgeRequest r)=>Result(dispatcher.HandleAsync(new(BridgeProtocol.Origin,r),CancellationToken.None));
            check(Send(Request("state")).Ok,"Extension reads shared desktop state with read-only verification");
            var first=Request("setSite","直连");var response=Send(first);long revision=engine.Revision;
            check(response.Ok&&response.Data!.Value.GetProperty("state").GetString()=="applying","Extension mutation acknowledges queued intent, not premature success");
            check(Field<DataGridView>(form,"_sites").Rows.Cast<DataGridViewRow>().Any(r=>r.Cells[0].Value?.ToString()=="video.example.com"),"Extension update synchronizes desktop website controls");
            check(Send(first).Ok&&engine.Revision==revision,"Duplicate native request does not execute twice");
            first.Mode="代理";check(Send(first).Code=="duplicate_conflict"&&engine.Revision==revision,"Reused request ID with changed content rejected");
            Send(Request("setSite","代理"));Send(Request("setSite","自动"));Wait(engine.FlushAsync());check(engine.Desired.Websites.Single().Mode=="自动"&&engine.StateCode=="applied","Rapid extension changes share serial last-intent queue");
            var local=AutoRouting.Copy(engine.Desired);local.Websites[0].Mode="直连";engine.Update(local);Wait(engine.FlushAsync());check(Send(Request("state")).Data!.Value.GetProperty("mode").GetString()=="直连","Desktop edits synchronize into next extension state");
            fake.Reject=true;Send(Request("setSite","代理"));Wait(engine.FlushAsync());check(Send(Request("state")).Data!.Value.GetProperty("state").GetString()=="retry","Controller rejection is visible as retry, never green success");
            Send(Request("retry"));Wait(engine.FlushAsync());check(engine.StateCode=="applied","Extension explicit retry uses same application executor");
            var conflict=Request("setSite");conflict.BaseRevision=0;check(Send(conflict).Code=="conflict","Stale base revision cannot replace newer desktop edit");
            var old=Request("setSite");old.Sequence=1;check(Send(old).Code=="stale_sequence","Older popup sequence cannot replace latest choice");
            var stale=Request("setSite");stale.Session=Guid.NewGuid().ToString("N");check(Send(stale).Code=="session_changed","Desktop restart session invalidates old mutation");
            var expired=Request("setSite");expired.ExpiresAt=0;revision=engine.Revision;check(Send(expired).Code=="expired"&&engine.Revision==revision,"Expired mutation rejected without side effects");
            var invalid=Request("runProgram");check(Send(invalid).Code=="unknown_operation","Unknown operation cannot run arbitrary command");
            var version=Request("state");version.Version=5;check(Send(version).Code=="version_mismatch","Unsupported protocol version rejected");
            check(Result(dispatcher.HandleAsync(new("chrome-extension://unapproved/",Request("state")),CancellationToken.None)).Code=="unauthorized_origin","Unapproved extension origin rejected by desktop boundary");
            var illegal=Request("setSite",domain:"edge://settings");check(Send(illegal).Code=="invalid_domain","Internal browser URL rejected by desktop compiler normalization");
            var template=Request("applyTemplate");template.Template="bilibili";template.Domains=["unreviewed.example.com"];check(Send(template).Code=="invalid_parameter","Template application restricted to reviewed template domains");
            var cancelled=new CancellationTokenSource();cancelled.Cancel();revision=engine.Revision;try{Wait(dispatcher.HandleAsync(new(BridgeProtocol.Origin,Request("setSite")),cancelled.Token));}catch(OperationCanceledException){}check(engine.Revision==revision,"Disconnected cancelled request cannot mutate desktop intent");cancelled.Dispose();
            bool unknownRejected=false;try{JsonSerializer.Deserialize<BridgeRequest>("{\"version\":1,\"path\":\"anything\"}",BridgeProtocol.Json);}catch(JsonException){unknownRejected=true;}check(unknownRejected,"Protocol rejects arbitrary file/path fields");
            using var framed=new MemoryStream();byte[] bytes=Encoding.UTF8.GetBytes("{\"测试\":\"你好\"}");Wait(BridgeProtocol.WriteAsync(framed,bytes,CancellationToken.None));framed.Position=0;check(Result(BridgeProtocol.ReadAsync(framed,CancellationToken.None))!.SequenceEqual(bytes),"Native framing uses UTF-8 byte count, not text length");
            using var oversized=new MemoryStream(BitConverter.GetBytes(BridgeProtocol.Limit+1));bool tooLarge=false;try{Wait(BridgeProtocol.ReadAsync(oversized,CancellationToken.None));}catch(InvalidDataException){tooLarge=true;}check(tooLarge,"Oversized native frame rejected before allocation");
            var checkType=typeof(RoutingForm).Assembly.GetType("ClashDock.CheckRoutingForm")!;using(var panel=(Form)Activator.CreateInstance(checkType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{client,engine,"example.com",false},null)!){panel.Show();Application.DoEvents();var observing=(Task)checkType.GetMethod("ObserveAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(panel,null)!;Application.DoEvents();var output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/desktop-enhanced"));Directory.CreateDirectory(output);using(var image=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(output,"inspection.png"));}panel.Close();Wait(observing);check(observing.IsCompleted,"Closing check panel cancels observation lifecycle");}
            var selector=Field<ModernSelect>(form,"_proxy"); selector.SetItems(cache.Snapshot.Select(t=>t.Name),"group"); Field<TextBox>(form,"_appSearch").Text=""; form.ClientSize=new(1280,900);Application.DoEvents();
            var outDir=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/desktop-enhanced"));Directory.CreateDirectory(outDir);
            using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(outDir,"main.png"));}
            typeof(ModernSelect).GetMethod("ToggleDropDown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(selector,null);Application.DoEvents();
            var drop=Field<ToolStripDropDown>(selector,"_dd");using(var image=new Bitmap(drop.Width,drop.Height)){drop.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(outDir,"targets.png"));}drop.Close();
            NativeTransport(check,dispatcher,(op,mode,domain)=>Request(op,mode,domain));
            form.Hide();dispatcher.Dispose();
        }
        Wait(engine.FlushAsync());engine.Stop();proxy.Dispose();
    }
    static void NativeTransport(Action<bool,string> check,BridgeDispatcher handler,Func<string,string,string,BridgeRequest> request)
    {
        string dir=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../Bridge/bin/Release/net9.0-windows/win-x64"));string exe=Path.Combine(dir,"ClashDock.Bridge.exe");
        using var control=new Control();_=control.Handle;var server=new DesktopBridge(control,handler,dir);
        try{
            using var proc=Process.Start(new ProcessStartInfo(exe,BridgeProtocol.Origin){RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true})!;
            var r=request("state","自动","example.com");Wait(BridgeProtocol.WriteAsync(proc.StandardInput.BaseStream,JsonSerializer.SerializeToUtf8Bytes(r,BridgeProtocol.Json),CancellationToken.None));var data=Result(BridgeProtocol.ReadAsync(proc.StandardOutput.BaseStream,CancellationToken.None));var reply=JsonSerializer.Deserialize<BridgeReply>(data!,BridgeProtocol.Json)!;
            check(reply.Ok&&reply.Id==r.Id,"Real native host forwards framed request to isolated desktop pipe");
            var write=request("setSite","直连","native.example.com");var payload=JsonSerializer.SerializeToUtf8Bytes(write,BridgeProtocol.Json);Wait(BridgeProtocol.WriteAsync(proc.StandardInput.BaseStream,payload,CancellationToken.None));var accepted=JsonSerializer.Deserialize<BridgeReply>(Result(BridgeProtocol.ReadAsync(proc.StandardOutput.BaseStream,CancellationToken.None))!,BridgeProtocol.Json)!;long committed=accepted.Data!.Value.GetProperty("revision").GetInt64();
            proc.StandardInput.Close();check(proc.WaitForExit(3000)&&proc.ExitCode==0,"Native bridge exits on extension disconnect without replay");
            using(var next=Process.Start(new ProcessStartInfo(exe,BridgeProtocol.Origin){RedirectStandardInput=true,RedirectStandardOutput=true,UseShellExecute=false,CreateNoWindow=true})!){
                Wait(BridgeProtocol.WriteAsync(next.StandardInput.BaseStream,payload,CancellationToken.None));var duplicate=JsonSerializer.Deserialize<BridgeReply>(Result(BridgeProtocol.ReadAsync(next.StandardOutput.BaseStream,CancellationToken.None))!,BridgeProtocol.Json)!;
                var read=request("state","自动","native.example.com");Wait(BridgeProtocol.WriteAsync(next.StandardInput.BaseStream,JsonSerializer.SerializeToUtf8Bytes(read,BridgeProtocol.Json),CancellationToken.None));var fresh=JsonSerializer.Deserialize<BridgeReply>(Result(BridgeProtocol.ReadAsync(next.StandardOutput.BaseStream,CancellationToken.None))!,BridgeProtocol.Json)!;
                check(duplicate.Ok&&fresh.Data!.Value.GetProperty("revision").GetInt64()==committed,"Native host reconnect reuses dedup result without repeating mutation");next.StandardInput.Close();next.WaitForExit(3000);
            }

            using var invalid=Process.Start(new ProcessStartInfo(exe,"chrome-extension://wrong/"){RedirectStandardInput=true,RedirectStandardOutput=true,UseShellExecute=false,CreateNoWindow=true})!;check(invalid.WaitForExit(3000)&&invalid.ExitCode==3,"Real native host refuses an unauthorized browser origin");
        }finally{Wait(server.StopAsync());}
    }
}
