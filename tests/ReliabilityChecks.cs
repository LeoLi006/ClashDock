using ClashDock;
using System.Text.Json;
internal static class ReliabilityChecks
{
    static T Result<T>(Task<T> task){Wait(task);return task.GetAwaiter().GetResult();}
    static void Wait(Task task){var until=Environment.TickCount64+15000;while(!task.IsCompleted&&Environment.TickCount64<until){Application.DoEvents();Thread.Sleep(3);}if(!task.IsCompleted)throw new Exception("fixture timeout");task.GetAwaiter().GetResult();}
    public static void Run(Action<bool,string> check)
    {
        ApplicationConfiguration.Initialize();
        var temp=Path.Combine(Path.GetTempPath(),"ClashDock-reliability-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try {
            var verge=Path.Combine(temp,"verge");Directory.CreateDirectory(Path.Combine(verge,"profiles"));File.WriteAllText(Path.Combine(verge,"profiles.yaml"),"items:\n- uid: Script\n  file: Script.js\n");const string user="function main(c){c.user=true;return c;}";File.WriteAllText(Path.Combine(verge,"profiles","Script.js"),user);File.WriteAllText(Path.Combine(verge,"clash-verge.yaml"),FeatureChecks.Fake.Source);
            var fake=new FeatureChecks.Fake();using var client=new MihomoClient("127.0.0.1",1,"",fake);
            var settings=new Settings{Routing=new(){VergeDirectory=verge,Proxy="group"}};
            var intent=Path.Combine(temp,"intent.json");var engine=new AutoRouting(client,settings,intent,()=>{});
            var desired=new RoutingSettings{VergeDirectory=verge,Proxy="group",Websites=[new(){Target="example.com",Mode="直连"}],Applications=[new(){Target="app.exe",Mode="代理"}]};
            check(fake.Writes==0,"Initial state has no network mutation");engine.Update(desired);
            check(engine.StateCode=="waiting"&&engine.IntentSaved&&fake.Writes==0,"Saved selection waits independently from rule acceptance");
            var loaded=new AutoRouting(client,settings,intent,()=>{});check(loaded.StateCode=="saved"&&loaded.Desired.Websites[0].Mode=="直连"&&fake.Writes==0,"Restart preserves pending intent without replay");loaded.Stop();
            fake.DelayMs=80;engine.Retry();check(engine.StateCode=="applying","Executing phase is explicit");Wait(engine.FlushAsync());fake.DelayMs=0;
            check(engine.StateCode=="applied"&&!File.Exists(intent),"Apply requires controller acceptance before loaded status");
            var inspector=new RoutingInspector(client,engine);var verified=Result(inspector.VerifyAsync("example.com",false,CancellationToken.None));
            check(verified.RulesAccepted==true&&verified.ProblemCode=="applied","Rule and selector acceptance are independently verified");
            int writes=fake.Writes;fake.Mode="global";var global=Result(inspector.VerifyAsync("example.com",false,CancellationToken.None));check(global.RulesAccepted==true&&global.ProblemCode=="mode_bypass"&&fake.Writes==writes,"Global mode is not reported as overwritten and never auto switched");
            engine.Retry();Wait(engine.FlushAsync());check(engine.FailureCode=="mode_bypass"&&fake.Mode=="global"&&fake.Writes==writes,"Apply in global mode refuses before changing network");
            fake.Mode="rule";engine.Retry();Wait(engine.FlushAsync());
            var good=fake.Live;fake.Live=FeatureChecks.Fake.Source;var missing=Result(inspector.VerifyAsync("example.com",false,CancellationToken.None));check(missing.ProblemCode=="overwritten"&&missing.RulesAccepted==false,"Missing managed rules have distinct diagnosis");fake.Live=good;
            fake.Selections[RoutingCompiler.DirectGroup]="node";var changed=Result(inspector.VerifyAsync("example.com",false,CancellationToken.None));check(changed.ProblemCode=="target_mismatch","Unexpected direct group choice cannot masquerade as DIRECT");fake.Selections.Remove(RoutingCompiler.DirectGroup);
            fake.Live=good.Replace("MATCH,DIRECT","DOMAIN,other.example,DIRECT\n- MATCH,DIRECT");engine.Retry();Wait(engine.FlushAsync());
            check(engine.StateCode=="retry"&&engine.FailureCode=="subscription_mismatch"&&engine.FailureMessage.Contains("未覆盖"),"Failure containing overwrite word retains typed failure classification");
            var failure=RoutingState.Describe(engine,missing);check(failure.Code=="retry"&&failure.Reason=="subscription_mismatch","Verification cannot hide true application failure");
            fake.Live=good;engine.Retry();Wait(engine.FlushAsync());
            var bad=AutoRouting.Copy(desired);bad.Proxy="removed-target";engine.Update(bad);Wait(engine.FlushAsync());check(engine.FailureCode=="target_missing"&&engine.Desired.Proxy=="removed-target","Removed target retains name and intent");
            engine.Update(desired);Wait(engine.FlushAsync());
            fake.Offline=true;var offline=Result(inspector.VerifyAsync("example.com",false,CancellationToken.None));check(!offline.Connected&&offline.ProblemCode=="controller_offline","Controller failure is distinct from missing rules");fake.Offline=false;
            var cannotSave=new AutoRouting(client,settings,Path.Combine(temp,"absent","intent.json"),()=>{});writes=fake.Writes;cannotSave.Update(desired);check(cannotSave.StateCode=="save_failed"&&!cannotSave.IntentSaved&&fake.Writes==writes,"Save failure retains unsaved intent without network writes");cannotSave.Stop();
            using var dispatcher=new BridgeDispatcher(client,engine,_=>{});long sequence=0;var caller=Guid.NewGuid().ToString("N");
            BridgeRequest Request(string op,bool confirm=false)=>new(){Version=1,Id=Guid.NewGuid().ToString("N"),Client=caller,Op=op,Domain="example.com",Session=dispatcher.Session,BaseRevision=engine.Revision,Sequence=++sequence,ConfirmAll=confirm,ExpiresAt=DateTimeOffset.UtcNow.AddSeconds(15).ToUnixTimeMilliseconds()};
            BridgeReply Send(BridgeRequest r,bool browser=false)=>Result(dispatcher.HandleAsync(new(BridgeProtocol.Origin,r,browser),CancellationToken.None));
            var state=Send(Request("state"));var data=state.Data!.Value;
            check(data.GetProperty("applicationCount").GetInt32()==1&&data.GetProperty("websiteCount").GetInt32()==1&&data.GetProperty("intentSaved").GetBoolean(),"Retry preview exposes counts without rule identities");
            writes=fake.Writes;var revision=engine.Revision;var refused=Send(Request("retry"));check(!refused.Ok&&refused.Code=="confirmation_required"&&fake.Writes==writes&&engine.Revision==revision,"Unconfirmed all-rules retry cannot execute");
            var stale=Request("retry",true);stale.BaseRevision--;check(Send(stale).Code=="conflict"&&fake.Writes==writes,"Stale preview cannot apply changed configuration");
            var wrongSession=Request("retry",true);wrongSession.Session=Guid.NewGuid().ToString("N");check(Send(wrongSession).Code=="session_changed"&&fake.Writes==writes,"Restarted session rejects old mutation without replay");
            var approved=Request("retry",true);var accepted=Send(approved);Wait(engine.FlushAsync());revision=engine.Revision;var again=Send(approved);check(accepted.Ok&&again.Ok&&engine.Revision==revision,"Confirmed retry uses original queue and duplicate is not rerun");
            var stamp1=Send(Request("state")).Data!.Value.GetProperty("snapshot").GetInt64();var stamp2=Send(Request("state")).Data!.Value.GetProperty("snapshot").GetInt64();check(stamp2>stamp1,"Read snapshots carry monotonic generation within session");
            check(BridgeHealth.LastBrowserRequest==null,"Direct bridge caller is not recorded as real Edge handshake");Send(Request("state"),true);check(BridgeHealth.LastBrowserRequest!=null&&BridgeHealth.BrowserSummary.Contains("历史记录"),"Browser ancestry evidence is dated and never current-connection claim (simulated evidence)");
            fake.Connections=n=>n==1?new { connections=new object[]{new { id="short", metadata=new { process="app.exe",processPath=@"C:\Apps\app.exe",host="cdn.example.com" },rule="DomainSuffix",rulePayload="example.com",chains=new[]{"DIRECT"}},new { id="not-same-domain",metadata=new { process="app.exe",processPath=@"C:\Other\app.exe",host="notexample.com" },rule="Match",rulePayload="",chains=new[]{"node"}}}}:new { connections=Array.Empty<object>() };fake.ConnectionReads=0;
            var observer=new ConnectionObserver(client);Wait(observer.ObserveAsync("example.com",false,TimeSpan.FromMilliseconds(70),()=>{},CancellationToken.None,5));check(observer.Records.Count==1&&observer.Records.Single().Row.Id=="short","Observation retains short connection and rejects domain substring match");
            var candidate=new ConnectionRecord("x","","app.exe","","","",[]);check(ConnectionMatch.Match(candidate,@"C:\Apps\app.exe",true)=="candidate","Missing process path is explicitly unconfirmed name candidate");
            fake.ConnectionDelayMs=2000;using(var cancel=new CancellationTokenSource(40)){var watch=System.Diagnostics.Stopwatch.StartNew();try{Wait(observer.ObserveAsync("example.com",false,TimeSpan.FromSeconds(15),()=>{},cancel.Token));}catch(OperationCanceledException){}check(watch.ElapsedMilliseconds<600,"Stop cancels in-flight observation promptly");}fake.ConnectionDelayMs=0;
            var type=typeof(RoutingForm).Assembly.GetType("ClashDock.CheckRoutingForm")!;
            using(var panel=(Form)Activator.CreateInstance(type,client,engine,"example.com",false)!) {
                panel.Show();Application.DoEvents();var verify=type.GetMethod("VerifyAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;Wait((Task)verify.Invoke(panel,null)!);
                var output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/reliability-ui"));Directory.CreateDirectory(output);
                using(var image=new System.Drawing.Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(image,new System.Drawing.Rectangle(System.Drawing.Point.Empty,image.Size));image.Save(Path.Combine(output,"inspection.png"));}
                fake.ConnectionDelayMs=2000;var observe=(Task)type.GetMethod("ObserveAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(panel,null)!;panel.Close();Wait(observe);check(observe.IsCompleted,"Closing check panel cancels observation and discards old callbacks");fake.ConnectionDelayMs=0;
            }
            using(var main=new RoutingForm(client,settings,true,engine)) {
                main.Show();Application.DoEvents();var output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/reliability-ui"));
                using(var image=new System.Drawing.Bitmap(main.Width,main.Height)){main.DrawToBitmap(image,new System.Drawing.Rectangle(System.Drawing.Point.Empty,image.Size));image.Save(Path.Combine(output,"desktop.png"));}
                var retry=(Button)typeof(RoutingForm).GetField("_apply",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;check(main.RectangleToScreen(main.ClientRectangle).Contains(retry.RectangleToScreen(retry.ClientRectangle))&&retry.Text=="重新应用全部分流","Desktop all-rules retry button fits and names scope");main.Close();
            }
            engine.TogglePause();Wait(engine.FlushAsync());var paused=Result(inspector.VerifyAsync("example.com",false,CancellationToken.None));check(paused.ProblemCode=="paused"&&paused.RulesAccepted==true,"Pause is separate from missing rules and confirms managed withdrawal");
            writes=fake.Writes;engine.Update(desired);Wait(engine.FlushAsync());check(engine.StateCode=="saved"&&fake.Writes==writes,"Edits while paused are saved without claiming latest rules loaded");engine.TogglePause();Wait(engine.FlushAsync());
            var legacy=JsonSerializer.Deserialize<Settings>("{\"Host\":\"127.0.0.1\",\"Port\":9097,\"ForceRuleModeOnStart\":true,\"Routing\":{\"Websites\":[{\"Target\":\"example.com\",\"Mode\":\"直连\"}]}}");check(legacy!.Routing.Websites[0].Mode=="直连"&&!legacy.Paused,"Legacy settings preserve routes without introducing state migration");
            var repository=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));var bridgeFixture=Path.Combine(temp,"中文安装目录");Directory.CreateDirectory(Path.Combine(bridgeFixture,"BridgeTools"));Directory.CreateDirectory(Path.Combine(bridgeFixture,"EdgeExtension"));Directory.CreateDirectory(Path.Combine(bridgeFixture,"native-host"));
            File.Copy(Path.Combine(repository,"BridgeTools","Check-Bridge.ps1"),Path.Combine(bridgeFixture,"BridgeTools","Check-Bridge.ps1"));File.Copy(Path.Combine(repository,"EdgeExtension","manifest.json"),Path.Combine(bridgeFixture,"EdgeExtension","manifest.json"));File.WriteAllText(Path.Combine(bridgeFixture,"ClashDock.exe"),"");File.WriteAllText(Path.Combine(bridgeFixture,"ClashDock.Bridge.exe"),"");
            File.WriteAllText(Path.Combine(bridgeFixture,"native-host","com.clashdock.bridge.json"),JsonSerializer.Serialize(new{name="com.clashdock.bridge",type="stdio",path=Path.Combine(bridgeFixture,"ClashDock.Bridge.exe"),allowed_origins=new[]{BridgeProtocol.Origin}}));
            string installation=Result(BridgeHealth.RunScript("Check-Bridge.ps1",bridgeFixture,CancellationToken.None));check(installation.Contains("HostPathMatches")&&installation.Contains("这是只读安装检查")&&!installation.Contains('�'),"Embedded script preserves UTF8 output and Chinese installation paths (read-only)");
            var tools=Path.Combine(AppContext.BaseDirectory,"BridgeTools");Directory.CreateDirectory(tools);var embedded=Path.Combine(tools,"Check-Bridge.ps1");var oldScript=File.Exists(embedded)?File.ReadAllBytes(embedded):null;File.Copy(Path.Combine(repository,"BridgeTools","Check-Bridge.ps1"),embedded,true);
            bool healthRendered=false;using(var owner=new Form())using(var close=new System.Windows.Forms.Timer{Interval=100}) {
                var limit=Environment.TickCount64+10000;
                close.Tick+=(_,_)=>{var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(x=>x.Text=="Edge 通信桥 · 当前安装");if(dialog==null)return;var body=dialog.Controls.OfType<TextBox>().FirstOrDefault()?.Text??"";if(body.Contains("HKCU 指向本安装")||Environment.TickCount64>limit){healthRendered=body.Contains("HKCU 指向本安装")&&body.Contains("安装检查不等于真实浏览器握手");var output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/reliability-ui"));using(var image=new System.Drawing.Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new System.Drawing.Rectangle(System.Drawing.Point.Empty,image.Size));image.Save(Path.Combine(output,"bridge-health.png"));}close.Stop();dialog.Close();}};
                close.Start();try{typeof(BridgeHealth).GetMethod("Show",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,[owner]);}finally{close.Stop();if(oldScript==null)File.Delete(embedded);else File.WriteAllBytes(embedded,oldScript);}
            }
            check(healthRendered,"Bridge health dialog renders independent installation and historical handshake layers (isolated WinForms)");
            engine.Stop();
            Console.WriteLine("Reliability fixture completed; all writes were to simulated controller.");
        } finally {Directory.Delete(temp,true);}
    }
}
