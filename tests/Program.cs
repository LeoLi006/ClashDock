using ClashDock;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using YamlDotNet.RepresentationModel;

internal static class Checks
{
    private static int _count;
    private static void Check(bool pass, string name)
    {
        if (!pass) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name); _count++;
    }
    private static void Reject(Action action, string name)
    {
        try { action(); } catch (ArgumentException) { Check(true, name); return; }
        throw new Exception("FAIL: " + name);
    }

    [STAThread]
    private static void Main()
    {
        var draft = new RoutingSettings
        {
            Proxy = "代理组",
            Applications = [new() { Target = @"C:\Apps\Example.exe", Mode = "代理" }, new() { Target = "chat.exe", Mode = "自动" }],
            Websites = [new() { Target = "example.com", Mode = "代理" }, new() { Target = "video.example.com", Mode = "直连" }]
        };
        Check(RoutingCompiler.Domain("https://WWW.Example.com/path?q=1") == "www.example.com", "URL normalization");
        Check(RoutingCompiler.Domain("*.example.com") == "example.com", "Wildcard normalization");
        Check(RoutingCompiler.Domain("例子.中国").StartsWith("xn--"), "IDN normalization");
        Reject(() => RoutingCompiler.Domain("127.0.0.1"), "Reject IP domain rules");
        Reject(() => RoutingCompiler.Domain("file:///example.com"), "Reject non-HTTP schemes");
        var rules = RoutingCompiler.Rules(draft);
        Check(rules.Count == 3 && rules[0].StartsWith("DOMAIN-SUFFIX,video.example.com,"), "Specific sites before parent and application");
        Check(rules[2].StartsWith("PROCESS-PATH,C:\\Apps\\Example.exe,"), "Exact application path rule");
        draft.Applications.Add(new() { Target = "bad,name.exe", Mode = "直连" });
        Reject(() => RoutingCompiler.Rules(draft), "Reject rule injection");
        draft.Applications.RemoveAt(2);
        draft.Websites.Add(new() { Target = "EXAMPLE.com", Mode = "自动" });
        Reject(() => RoutingCompiler.Rules(draft), "Reject duplicate domains");
        draft.Websites.RemoveAt(2);
        const string yaml = "mode: global\nfind-process-mode: strict\nproxies: []\nproxy-groups:\n- name: 代理组\n  type: select\n  proxies: [DIRECT]\nrules:\n- DOMAIN-SUFFIX,example.com,DIRECT\n- MATCH,DIRECT\n";
        var runtime = RoutingCompiler.Runtime(yaml, rules, draft.Proxy);
        var repeated = RoutingCompiler.Runtime(runtime, rules, draft.Proxy);
        var map = RoutingCompiler.ReadYaml(repeated);
        Check(((YamlSequenceNode)map.Children[new YamlScalarNode("rules")]).Children.Count == 5, "Repeated apply does not duplicate rules");
        Check(((YamlSequenceNode)map.Children[new YamlScalarNode("proxy-groups")]).Children.Count == 3, "Repeated apply does not duplicate groups");
        var cleared = RoutingCompiler.ReadYaml(RoutingCompiler.Runtime(repeated, [], ""));
        Check(((YamlSequenceNode)cleared.Children[new YamlScalarNode("rules")]).Children.Count == 2, "Remove managed rules, retain original domain rule");
        Check(((YamlSequenceNode)cleared.Children[new YamlScalarNode("proxy-groups")]).Children.Count == 1, "Remove managed groups, retain user group");
        var localRuntime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "io.github.clash-verge-rev.clash-verge-rev", "clash-verge.yaml");
        if (File.Exists(localRuntime))
        {
            var source = File.ReadAllText(localRuntime);
            var before = RoutingCompiler.ReadYaml(source);
            var after = RoutingCompiler.ReadYaml(RoutingCompiler.Runtime(source, rules, draft.Proxy));
            var changedKeys = new HashSet<string> { "rules", "mode", "find-process-mode", "proxy-groups" };
            Check(before.Children.Where(p => !changedKeys.Contains(p.Key.ToString())).All(p => after.Children.TryGetValue(p.Key, out var value) && value.Equals(p.Value)), "Real local YAML round trip preserves all unrelated fields (read only)");
            Check(File.ReadAllText(localRuntime) == source, "Actual Verge runtime file unchanged");
        }
        var script = RoutingCompiler.Script("function main(c) { c.original = true; return c; }", rules, draft.Proxy);
        Check(RoutingCompiler.Script(script, rules, draft.Proxy) == script, "Script installation idempotent");
        Check(!RoutingCompiler.Script(script, [], "").Contains(RoutingCompiler.Begin), "Script uninstall removes only managed section");
        var temp = Path.Combine(Path.GetTempPath(), "ClashDock-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var jsPath = Path.Combine(temp, "verify.js");
            File.WriteAllText(jsPath, script + "\nconst c = main({rules:['MATCH,DIRECT']},'test'); if (!c.original || c.rules.length !== 4 || c['proxy-groups'].length !== 2) process.exit(1);\n");
            using var node = Process.Start(new ProcessStartInfo("node", jsPath) { UseShellExecute = false, CreateNoWindow = true })!;
            node.WaitForExit(); Check(node.ExitCode == 0, "Generated JavaScript executes and preserves original main");
            IntegrationAsync(temp, yaml, draft).GetAwaiter().GetResult();
            ApplicationConfiguration.Initialize();
            var client = new MihomoClient("127.0.0.1", 1, "");
            using var form = new RoutingForm(client, new Settings { Routing = draft }, preview: true);
            form.Show();
            Application.DoEvents();
            var root = form.Controls.OfType<TableLayoutPanel>().Single();
            IEnumerable<Control> Descendants(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
            var apply = Descendants(root).OfType<Button>().Single(b => b.Text == "保存并应用");
            Check(form.RectangleToScreen(form.ClientRectangle).Contains(apply.RectangleToScreen(apply.ClientRectangle)), "Apply button fits visible client area");
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            var artifact = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/routing-preview.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(artifact)!); image.Save(artifact);
            form.Hide();
            Check(true, "Routing form renders without API mutations");
            Console.WriteLine("Preview: " + artifact);
        }
        finally { Directory.Delete(temp, true); }
        Console.WriteLine($"{_count} checks passed.");
    }

    private static async Task IntegrationAsync(string temp, string yaml, RoutingSettings draft)
    {
        var directory = Path.Combine(temp, "verge"); Directory.CreateDirectory(Path.Combine(directory, "profiles"));
        var scriptPath = Path.Combine(directory, "profiles", "Script.js");
        const string original = "function main(c) { return c; }";
        await File.WriteAllTextAsync(scriptPath, original);
        await File.WriteAllTextAsync(Path.Combine(directory, "profiles.yaml"), "items:\n- uid: Script\n  type: script\n  file: Script.js\n");
        await File.WriteAllTextAsync(Path.Combine(directory, "clash-verge.yaml"), yaml);
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        var live = yaml; var rejectNext = false; var mode = "global";
        using var cancel = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); } catch { break; }
                var response = "{}";
                var request = context.Request;
                if (request.Url!.AbsolutePath == "/proxies")
                    response = "{\"proxies\":{\"代理组\":{\"type\":\"Selector\",\"now\":\"node\",\"all\":[\"node\"]},\"node\":{\"type\":\"Shadowsocks\"}}}";
                else if (request.Url.AbsolutePath == "/configs" && request.HttpMethod == "GET") response = JsonSerializer.Serialize(new { mode });
                else if (request.Url.AbsolutePath == "/configs" && request.HttpMethod == "PUT")
                {
                    using var payload = JsonDocument.Parse(await new StreamReader(request.InputStream).ReadToEndAsync());
                    if (rejectNext) { rejectNext = false; context.Response.StatusCode = 400; }
                    else { live = payload.RootElement.GetProperty("payload").GetString()!; mode = "rule"; }
                }
                else if (request.Url.AbsolutePath == "/configs" && request.HttpMethod == "PATCH")
                {
                    using var payload = JsonDocument.Parse(await new StreamReader(request.InputStream).ReadToEndAsync()); mode = payload.RootElement.GetProperty("mode").GetString()!;
                }
                else if (request.Url.AbsolutePath == "/rules")
                {
                    var map = RoutingCompiler.ReadYaml(live);
                    response = JsonSerializer.Serialize(new { rules = ((YamlSequenceNode)map.Children[new YamlScalarNode("rules")]).Children.Select(r =>
                    {
                        var p = r.ToString().Split(','); return new { type = p[0] switch { "DOMAIN-SUFFIX" => "DomainSuffix", "PROCESS-PATH" => "ProcessPath", _ => "Match" }, payload = p[1], proxy = p[^1] };
                    }) });
                }
                var bytes = Encoding.UTF8.GetBytes(response); context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
            }
        });
        // 独立进程目录中的 settings.json，不接触用户的发布配置。
        var settings = new Settings { Host = "127.0.0.1", Port = port, Routing = new RoutingSettings() };
        var client = new MihomoClient("127.0.0.1", port, ""); var service = new RoutingService(client, settings);
        draft.VergeDirectory = directory;
        try
        {
            await service.ApplyAsync(draft);
            Check((await File.ReadAllTextAsync(scriptPath)).Contains(RoutingCompiler.Begin), "Service installs persistent global script");
            Check(settings.Routing == draft && mode == "rule", "Service verifies API rules and saves settings");
            var installed = await File.ReadAllTextAsync(scriptPath);
            rejectNext = true;
            var next = new RoutingSettings { VergeDirectory = directory, Websites = [new() { Target = "other.example.com", Mode = "直连" }] };
            var failed = false; try { await service.ApplyAsync(next); } catch (InvalidOperationException) { failed = true; }
            Check(failed && await File.ReadAllTextAsync(scriptPath) == installed, "Rejected configuration restores original script");
            Check(settings.Routing == draft && live.Contains("video.example.com"), "Rejected configuration restores previous routing state");
            await service.ApplyAsync(new RoutingSettings { VergeDirectory = directory });
            Check(!(await File.ReadAllTextAsync(scriptPath)).Contains(RoutingCompiler.Begin) && !live.Contains(RoutingCompiler.DirectGroup), "Empty configuration uninstalls managed routing");
        }
        finally { cancel.Cancel(); listener.Stop(); await server; }
    }
}
