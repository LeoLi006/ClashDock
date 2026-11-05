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

            ApplicationConfiguration.Initialize();
            FeatureChecks.Run(Check, temp);
            var client = new MihomoClient("127.0.0.1", 1, "");
            using var form = new RoutingForm(client, new Settings { Routing = draft }, preview: true);
            form.Show();
            Application.DoEvents();
            var root = form.Controls.OfType<TableLayoutPanel>().Single();
            IEnumerable<Control> Descendants(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
            var apply = Descendants(root).OfType<Button>().Single(b => b.Text == "重试应用");
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

}
