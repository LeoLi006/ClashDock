using ClashDock;
using System.Net;
using System.Text;
using System.Text.Json;
using YamlDotNet.RepresentationModel;

internal static class FeatureChecks
{
    public sealed class Fake : HttpMessageHandler
    {
        public const string Source = "mode: rule\nproxies: []\nproxy-groups:\n- name: group\n  type: select\n  proxies: [node,other]\nrules:\n- MATCH,DIRECT\n";
        public string Live = Source;
        public string Mode = "rule";
        public bool Reject, Offline;
        public int Reloads, Writes, Active, MaxActive, SelectionWrites;
        public readonly Dictionary<string,string> Selections = new() { ["group"] = "node" };
        public int DelayMs;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (Offline) throw new HttpRequestException("offline");
            string path = req.RequestUri!.AbsolutePath; object result = new { }; int code = 200;
            if (req.Method != HttpMethod.Get) Writes++;
            if (path.EndsWith("/delay")) {
                if (path.Contains("timeout-probe")) return new HttpResponseMessage(HttpStatusCode.GatewayTimeout);
                if (path.Contains("fail-probe")) return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"message\":\"invalid fixture\"}") };
                int active = Interlocked.Increment(ref Active); MaxActive = Math.Max(MaxActive, active);
                try { await Task.Delay(30, ct); result = new { delay = 42 }; } finally { Interlocked.Decrement(ref Active); }
            }
            else if (path == "/proxies") {
                var proxies = new Dictionary<string,object> {
                    ["group"] = new { type = "Selector", now = Selections["group"], all = new[] { "node", "other" } },
                    ["node"] = new { type = "Shadowsocks", history = new[] { new { delay = 55, time = DateTimeOffset.UtcNow.AddMinutes(-20) } } },
                    ["other"] = new { type = "Shadowsocks", history = Array.Empty<object>() },
                    ["timeout"] = new { type = "Shadowsocks", history = new[] { new { delay = 0, time = DateTimeOffset.UtcNow } } }
                };
                var map = RoutingCompiler.ReadYaml(Live);
                if (map.Children.TryGetValue(new YamlScalarNode("proxy-groups"), out var groups)) foreach (var g in ((YamlSequenceNode)groups).Children.OfType<YamlMappingNode>()) {
                    string name = g.Children[new YamlScalarNode("name")].ToString();
                    var options = ((YamlSequenceNode)g.Children[new YamlScalarNode("proxies")]).Children.Select(x => x.ToString()).ToArray();
                    proxies[name] = new { type = "Selector", now = Selections.GetValueOrDefault(name, options.FirstOrDefault() ?? ""), all = options };
                }
                result = new { proxies };
            }
            else if (path.StartsWith("/proxies/") && req.Method == HttpMethod.Put) {
                using var doc = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));
                Selections[Uri.UnescapeDataString(path[9..])] = doc.RootElement.GetProperty("name").GetString()!; SelectionWrites++;
            }
            else if (path == "/configs" && req.Method == HttpMethod.Get) result = new { mode = Mode, tun = new { enable = false }, port = 0, mixed_port = 0 };
            else if (path == "/configs" && req.Method == HttpMethod.Put) {
                Reloads++; if (DelayMs > 0) await Task.Delay(DelayMs, ct);
                if (Reject) { Reject = false; code = 400; result = new { message = "injected rejection" }; }
                else { using var doc = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct)); Live = doc.RootElement.GetProperty("payload").GetString()!; Mode = RoutingCompiler.ReadYaml(Live).Children[new YamlScalarNode("mode")].ToString(); }
            }
            else if (path == "/configs" && req.Method.Method == "PATCH") { using var doc = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct)); Mode = doc.RootElement.GetProperty("mode").GetString()!; }
            else if (path == "/rules") {
                var map = RoutingCompiler.ReadYaml(Live);
                result = new { rules = ((YamlSequenceNode)map.Children[new YamlScalarNode("rules")]).Children.Select(n => {
                    var p = n.ToString().Split(','); return new { type = p[0] switch { "DOMAIN-SUFFIX" => "DomainSuffix", "PROCESS-PATH" => "ProcessPath", "PROCESS-NAME" => "ProcessName", _ => "Match" }, payload = p[0] == "MATCH" ? "" : p[1], proxy = p[^1] };
                }) };
            }
            else if (path == "/connections") result = new { connections = new[] { new { metadata = new { process = "app.exe", processPath = @"C:\Apps\app.exe", host = "video.example.com", destinationIP = "1.2.3.4" }, rule = "DomainSuffix", rulePayload = "example.com", chains = new[] { "node", "group" } } } };
            return new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(JsonSerializer.Serialize(result), Encoding.UTF8, "application/json") };
        }
    }
    static void Wait(Task task) { var until = DateTime.UtcNow.AddSeconds(15); while (!task.IsCompleted && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(5); } if (!task.IsCompleted) throw new Exception("Test timed out"); task.GetAwaiter().GetResult(); }
    static void Spin(int ms) { var until = Environment.TickCount64 + ms; while (Environment.TickCount64 < until) { Application.DoEvents(); Thread.Sleep(5); } }
    public static void Run(Action<bool,string> check, string temp)
    {
        var root = Path.Combine(temp, "fake-verge"); Directory.CreateDirectory(Path.Combine(root,"profiles"));
        string script = Path.Combine(root,"profiles","Script.js"); const string user = "function main(c) { c.user = true; return c; }";
        File.WriteAllText(script,user); File.WriteAllText(Path.Combine(root,"profiles.yaml"),"items:\n- uid: Script\n  file: Script.js\n"); File.WriteAllText(Path.Combine(root,"clash-verge.yaml"),Fake.Source);
        var fake = new Fake(); using var client = new MihomoClient("127.0.0.1", 1, "", fake);
        var settings = new Settings { Routing = new RoutingSettings { VergeDirectory = root } };
        string intent = Path.Combine(temp,"intent.json"); int saves = 0;
        var engine = new AutoRouting(client, settings, intent, () => saves++);
        RoutingSettings Draft(string mode, string proxy = "group") => new() { VergeDirectory = root, Proxy = proxy, Applications = [new() { Target = @"C:\Apps\app.exe", Mode = mode }], Websites = [new() { Target = "example.com", Mode = "直连" }] };
        check(fake.Writes == 0, "Initial construction performs no network writes");
        engine.Update(Draft("代理")); engine.Update(Draft("直连")); engine.Update(Draft("代理"));
        check(File.Exists(intent) && fake.Writes == 0, "Intent durable before debounce");
        Spin(600); Wait(engine.FlushAsync()); check(fake.Reloads == 1 && settings.Routing.Applications[0].Mode == "代理", "Debounce collapses rapid edits to last choice");
        check(engine.Status.StartsWith("已生效") && !File.Exists(intent), "Applied only after rule and selector verification");
        int reloads = fake.Reloads; engine.Update(Draft("代理","other")); Wait(engine.FlushAsync());
        check(fake.Reloads == reloads && fake.Selections[RoutingCompiler.ProxyGroup] == "other", "Proxy target uses selector without full reload");
        fake.DelayMs = 100; engine.Update(Draft("直连")); engine.Retry(); engine.Update(Draft("自动")); Wait(engine.FlushAsync());
        check(settings.Routing.Applications[0].Mode == "自动" && engine.Status.StartsWith("已生效"), "Edits during apply serialize to latest revision"); fake.DelayMs = 0;
        var previous = File.ReadAllText(script); fake.Reject = true; engine.Update(Draft("直连")); Wait(engine.FlushAsync());
        check(engine.Status.Contains("失败") && File.Exists(intent) && engine.Desired.Applications[0].Mode == "直连", "Failure preserves durable user intent");
        check(File.ReadAllText(script) == previous && settings.Routing.Applications[0].Mode == "自动", "Rejected reload restores user script and prior applied state");
        engine.Retry(); Wait(engine.FlushAsync()); check(engine.Status.StartsWith("已生效"), "Explicit retry recovers failed application");
        fake.Live = Fake.Source; Wait(engine.VerifyAsync(client)); check(engine.Status.Contains("覆盖"), "Verge overwrite detected without automatic repair");
        int writes = fake.Writes; Wait(engine.VerifyAsync(client)); check(fake.Writes == writes, "Verification never competes with Verge");
        engine.Retry(); Wait(engine.FlushAsync());
        fake.Mode = "global";
        engine.TogglePause(); Wait(engine.FlushAsync()); check(fake.Mode == "global", "Pause preserves existing global mode"); check(settings.Paused && !File.ReadAllText(script).Contains(RoutingCompiler.Begin) && fake.Live.Contains("MATCH,DIRECT"), "Pause removes only owned rules and preserves subscription");
        var restarted = new AutoRouting(client, settings, Path.Combine(temp,"restart.json"), () => {});
        check(restarted.Paused && restarted.Desired.Applications.Count == 1, "Paused restart retains resume configuration"); restarted.Stop();
        int pausedWrites = fake.Writes; engine.Update(Draft("直连")); Wait(engine.FlushAsync()); check(fake.Writes == pausedWrites && engine.Status.Contains("待核验"), "Editing while paused saves without controller mutations or unverified success");
        engine.TogglePause(); Wait(engine.FlushAsync()); check(!settings.Paused && File.ReadAllText(script).Contains(RoutingCompiler.Begin), "Resume restores retained routing");
        engine.Update(Draft("代理")); Wait(engine.FlushAsync()); engine.Undo(); Wait(engine.FlushAsync()); check(settings.Routing.Applications[0].Mode == "直连", "Undo restores previous successful routing intent");
        for (int i=0;i<7;i++) { engine.Update(Draft(i%2==0 ? "代理":"直连")); Wait(engine.FlushAsync()); }
        check(Directory.GetFiles(Path.GetDirectoryName(script)!,"Script.js.clashdock-*.bak").Length <= 5, "Script backup retention bounded to five");
        engine.RemoveAll(); Wait(engine.FlushAsync()); check(!File.ReadAllText(script).Contains(RoutingCompiler.Begin) && File.ReadAllText(script).Contains("c.user = true"), "Remove-all preserves user's original script");
        fake.Offline = true; engine.Update(Draft("直连")); Wait(engine.FlushAsync()); check(File.Exists(intent) && !engine.Status.StartsWith("已生效"), "Offline apply never reports success");
        var recovered = new AutoRouting(client, settings, intent, () => {}); check(recovered.Desired.Applications.Count == 1 && recovered.Status.Contains("待重试"), "Pending intent restored without automatic network mutation"); recovered.Stop();
        engine.Stop(); fake.Offline = false;
        var targets = client.GetTargetsAsync(); Wait(targets);
        check(targets.Result.Single(x=>x.Name=="other").Delay == null, "Missing history is untested, never timeout");
        check(targets.Result.Single(x=>x.Name=="timeout").Delay == 0, "Actual zero-delay history is timeout");
        check(targets.Result.Single(x=>x.Name=="group").Summary.Contains("陈旧"), "Group selected-node history carries stale timestamp");
        var diag=client.DiagnoseAsync("example.com",false); Wait(diag); check(diag.Result.Contains("node") && diag.Result.Contains("DomainSuffix"), "Diagnostics reports actual rule and exit chain");
        var empty=client.DiagnoseAsync("missing.example",false); Wait(empty); check(empty.Result.Contains("尚未检测到") && empty.Result.Contains("不能证明"), "Absent connections do not assert routing failure or success");
        using var cancel = new CancellationTokenSource(); var measure=client.MeasureAsync("node",cancel.Token); Wait(measure); check(measure.Result.Contains("42 ms"), "Async latency query parses controller result");
        var timed=client.MeasureAsync("timeout-probe",cancel.Token); Wait(timed); check(timed.Result.Contains("节点测速超时"), "Gateway timeout classified as node test timeout");
        var failure=client.MeasureAsync("fail-probe",cancel.Token); Wait(failure); check(failure.Result.StartsWith("测速失败"), "Controller test rejection classified separately");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var stopped=client.MeasureAsync("node",cancelled.Token); Wait(stopped); check(stopped.Result == "已取消", "Latency request cancellation reported accurately");
        var targetType=typeof(RoutingForm).Assembly.GetType("ClashDock.TargetForm")!;
        using (var targetForm=(Form)Activator.CreateInstance(targetType, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new object[]{client}, null)!) {
            targetForm.Show(); Application.DoEvents();
            var tests=(Task)targetType.GetMethod("TestAsync",System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(targetForm,new object[]{true})!; Wait(tests);
            check(fake.MaxActive == 2, "All-target latency concurrency limited to two");
            targetForm.Close();
        }
        fake.Offline=true; var offline=client.MeasureAsync("node",cancel.Token); Wait(offline); check(offline.Result.Contains("控制器连接失败"), "Controller offline distinguished from node timeout");
        string blocked = Path.Combine(temp,"blocked-intent"); Directory.CreateDirectory(blocked);
        var blockedEngine = new AutoRouting(client,settings,blocked,()=>{}); blockedEngine.Update(Draft("直连"));
        bool exitBlocked=false; try { Wait(blockedEngine.FlushAsync()); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { exitBlocked=true; }
        check(saves > 0 && exitBlocked, "Checked persistence blocks exit if final intent cannot be saved"); blockedEngine.Stop();
    }
}

