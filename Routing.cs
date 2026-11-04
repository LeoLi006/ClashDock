using System.Globalization;
using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace ClashDock;

public sealed class RouteEntry
{
    public string Target { get; set; } = "";
    public string Mode { get; set; } = "自动";
}

public sealed class RoutingSettings
{
    public string VergeDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "io.github.clash-verge-rev.clash-verge-rev");
    public string Proxy { get; set; } = "";
    public List<RouteEntry> Applications { get; set; } = new();
    public List<RouteEntry> Websites { get; set; } = new();
    public List<string> AppliedRules { get; set; } = new();
}

/// <summary>生成规则，并通过 Verge 全局脚本持久化。没有虚拟网卡或流量劫持逻辑。</summary>
public static class RoutingCompiler
{
    public const string DirectGroup = "__ClashDock_DIRECT";
    public const string ProxyGroup = "__ClashDock_PROXY";
    public const string Begin = "// <ClashDock managed routing>";
    public const string End = "// </ClashDock managed routing>";

    public static string Domain(string input)
    {
        var text = input.Trim();
        if (text.StartsWith("*.")) text = text[2..];
        if (!Uri.TryCreate(text.Contains("://") ? text : "https://" + text, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http") || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("请输入网站域名或 http/https 网址。");
        var host = new IdnMapping().GetAscii(uri.Host.TrimEnd('.')).ToLowerInvariant();
        if (Uri.CheckHostName(host) != UriHostNameType.Dns || !host.Contains('.') || host.Contains(','))
            throw new ArgumentException("网站规则需要有效域名，例如 bilibili.com；不支持 IP 和网页路径规则。");
        return host;
    }

    public static List<string> Rules(RoutingSettings settings)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 网站优先；子域名优先于父域名。未指定的网站继续匹配应用和原订阅规则。
        foreach (var entry in settings.Websites.OrderByDescending(e => e.Target.Count(c => c == '.')))
            Add("DOMAIN-SUFFIX", Domain(entry.Target), entry.Mode);
        foreach (var entry in settings.Applications)
        {
            var target = entry.Target.Trim();
            var path = target.Contains('\\') || target.Contains('/');
            if (target.Length == 0 || target.IndexOfAny([',', '\r', '\n', '"', '*', '?']) >= 0
                || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || (path && !Path.IsPathFullyQualified(target)))
                throw new ArgumentException("应用必须是 exe 文件名或完整路径，且不能包含逗号或通配符。");
            Add(path ? "PROCESS-PATH" : "PROCESS-NAME", target, entry.Mode);
        }
        return result;

        void Add(string type, string target, string mode)
        {
            if (mode is not ("自动" or "直连" or "代理")) throw new ArgumentException("未知代理模式。");
            if (!seen.Add(type + "," + target)) throw new ArgumentException($"存在重复条目：{target}");
            if (mode == "自动") return;
            if (mode == "代理" && (string.IsNullOrWhiteSpace(settings.Proxy) || settings.Proxy.IndexOfAny([',', '\r', '\n']) >= 0))
                throw new ArgumentException("请先选择有效的代理目标。");
            var outbound = mode == "直连" ? DirectGroup : ProxyGroup;
            result.Add($"{type},{target},{outbound}");
        }
    }

    public static string StripScript(string source)
    {
        var start = source.IndexOf(Begin, StringComparison.Ordinal);
        if (start < 0) return source;
        var end = source.IndexOf(End, start, StringComparison.Ordinal);
        if (end < 0 || source.IndexOf(Begin, start + Begin.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("ClashDock 脚本标记损坏，请先恢复脚本备份。");
        return source[..start] + source[(end + End.Length)..];
    }

    public static string Script(string source, List<string> rules, string proxy)
    {
        var original = StripScript(source);
        if (rules.Count == 0) return original;
        // 在用户 main 之后包装，不改写用户已有脚本。
        return original.TrimEnd() + "\n\n" + Begin + "\n" +
            "var __clashDockMain = main;\n" +
            "main = function(config, profileName) {\n" +
            "  config = __clashDockMain(config, profileName);\n" +
            "  if (!config || typeof config !== 'object') throw new Error('Invalid original main result');\n" +
            "  config['proxy-groups'] = (config['proxy-groups'] || []).filter(function(g) { return g.name !== '" + DirectGroup + "' && g.name !== '" + ProxyGroup + "'; }).concat(" + JsonSerializer.Serialize(Groups(rules, proxy)) + ");\n" +
            "  config.rules = " + JsonSerializer.Serialize(rules) + ".concat(config.rules || []);\n" +
            "  config['find-process-mode'] = 'always';\n" +
            "  return config;\n};\n" + End + "\n";
    }

    public static YamlMappingNode ReadYaml(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return stream.Documents.Count == 1 && stream.Documents[0].RootNode is YamlMappingNode map
            ? map : throw new InvalidOperationException("配置必须是单个 YAML 对象。");
    }

    public static List<Dictionary<string, object>> Groups(List<string> rules, string proxy)
    {
        var groups = new List<Dictionary<string, object>>();
        foreach (var (name, target) in new[] { (DirectGroup, "DIRECT"), (ProxyGroup, proxy) })
            if (rules.Any(r => r.EndsWith("," + name, StringComparison.Ordinal)))
                groups.Add(new() { ["name"] = name, ["type"] = "select", ["proxies"] = new[] { target } });
        return groups;
    }

    public static string Runtime(string source, List<string> rules, string proxy)
    {
        var map = ReadYaml(source);
        var key = new YamlScalarNode("rules");
        var existing = map.Children.TryGetValue(key, out var node) && node is YamlSequenceNode seq
            ? seq.Children.OfType<YamlScalarNode>().Select(n => n.Value ?? "").ToList()
            : throw new InvalidOperationException("运行配置缺少规则列表。");
        var replacement = new YamlSequenceNode();
        foreach (var rule in rules.Concat(existing.Where(r => !r.EndsWith("," + DirectGroup, StringComparison.Ordinal) && !r.EndsWith("," + ProxyGroup, StringComparison.Ordinal))))
            replacement.Add(new YamlScalarNode(rule));
        map.Children[key] = replacement;
        var groupKey = new YamlScalarNode("proxy-groups");
        var groupList = map.Children.TryGetValue(groupKey, out var groupsNode) && groupsNode is YamlSequenceNode existingGroups
            ? existingGroups : new YamlSequenceNode();
        foreach (var group in groupList.Children.OfType<YamlMappingNode>().Where(g =>
            g.Children.TryGetValue(new YamlScalarNode("name"), out var name) && name.ToString() is DirectGroup or ProxyGroup).ToList())
            groupList.Children.Remove(group);
        foreach (var group in Groups(rules, proxy))
            groupList.Add(new YamlMappingNode
            {
                { "name", (string)group["name"] }, { "type", "select" },
                { "proxies", new YamlSequenceNode(((string[])group["proxies"]).Select(p => (YamlNode)new YamlScalarNode(p))) }
            });
        map.Children[groupKey] = groupList;
        map.Children[new YamlScalarNode("mode")] = new YamlScalarNode("rule");
        map.Children[new YamlScalarNode("find-process-mode")] = new YamlScalarNode("always");
        using var writer = new StringWriter();
        new YamlStream(new YamlDocument(map)).Save(writer, false);
        return writer.ToString();
    }
}

public sealed class RoutingService(MihomoClient client, Settings settings)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ApplyAsync(RoutingSettings draft)
    {
        await _gate.WaitAsync();
        try
        {
            if (settings.Host is not ("localhost" or "127.0.0.1" or "::1"))
                throw new InvalidOperationException("分流配置仅支持本机 Verge 控制器。");
            var rules = RoutingCompiler.Rules(draft);
            if (draft.Applications.Any(e => e.Mode == "代理") || draft.Websites.Any(e => e.Mode == "代理"))
            {
                var names = await client.GetProxyNamesAsync();
                if (!names.Contains(draft.Proxy)) throw new InvalidOperationException("代理目标已不存在，请重新选择节点或策略组。");
            }
            var directory = Path.GetFullPath(draft.VergeDirectory);
            var runtimePath = Path.Combine(directory, "clash-verge.yaml");
            var profiles = RoutingCompiler.ReadYaml(await File.ReadAllTextAsync(Path.Combine(directory, "profiles.yaml")));
            if (!profiles.Children.TryGetValue(new YamlScalarNode("items"), out var items) || items is not YamlSequenceNode list)
                throw new InvalidOperationException("无法识别 Verge 订阅索引。");
            var global = list.Children.OfType<YamlMappingNode>().FirstOrDefault(m =>
                m.Children.TryGetValue(new YamlScalarNode("uid"), out var uid) && uid.ToString() == "Script");
            if (global is null || !global.Children.TryGetValue(new YamlScalarNode("file"), out var file))
                throw new InvalidOperationException("未找到 Verge 全局扩展脚本。请在 Verge 中初始化全局扩展脚本。");
            var profilesDirectory = Path.GetFullPath(Path.Combine(directory, "profiles")) + Path.DirectorySeparatorChar;
            var scriptPath = Path.GetFullPath(Path.Combine(profilesDirectory, file.ToString()));
            if (!scriptPath.StartsWith(profilesDirectory, StringComparison.OrdinalIgnoreCase) || Path.GetExtension(scriptPath) != ".js")
                throw new InvalidOperationException("全局脚本路径无效。");
            var originalScript = await File.ReadAllTextAsync(scriptPath);
            var originalRuntime = await File.ReadAllTextAsync(runtimePath);
            var updatedScript = RoutingCompiler.Script(originalScript, rules, draft.Proxy);
            var updatedRuntime = RoutingCompiler.Runtime(originalRuntime, rules, draft.Proxy);
            var selections = await client.GetSelectorGroupsAsync();
            var previousMode = await client.GetModeAsync();
            var originalSettings = settings.Routing;
            // 备份仅包含全局脚本，不复制订阅；仅在用户点击应用时修改。
            var backup = scriptPath + ".clashdock-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".bak";
            File.Copy(scriptPath, backup, false);
            if (await File.ReadAllTextAsync(scriptPath) != originalScript)
                throw new InvalidOperationException("Verge 脚本被其他程序修改，请重试。");
            await AtomicWriteAsync(scriptPath, updatedScript);
            try
            {
                await client.ReloadAsync(updatedRuntime);
                var applied = await client.GetRuleKeysAsync();
                foreach (var rule in rules)
                {
                    var parts = rule.Split(',');
                    var type = parts[0] switch { "DOMAIN-SUFFIX" => "DomainSuffix", "PROCESS-PATH" => "ProcessPath", _ => "ProcessName" };
                    if (!applied.Contains($"{type}|{parts[1]}|{parts[2]}"))
                        throw new InvalidOperationException("内核未确认全部分流规则，已尝试恢复原配置。");
                }
                draft.AppliedRules = rules;
                settings.Routing = draft;
                settings.SaveChecked();
            }
            catch
            {
                settings.Routing = originalSettings;
                if (await File.ReadAllTextAsync(scriptPath) == updatedScript)
                    await AtomicWriteAsync(scriptPath, originalScript);
                try { await client.ReloadAsync(RoutingCompiler.Runtime(originalRuntime, originalSettings.AppliedRules, originalSettings.Proxy)); await client.SetModeAsync(previousMode); }
                catch { throw new InvalidOperationException("应用失败，原脚本已恢复；内核恢复未确认，请在 Verge 中重新启用当前订阅。"); }
                throw;
            }
            finally
            {
                // 内核重载可能重置组选择，尽量恢复原来的有效选择。
                foreach (var group in selections)
                    if (group.Name is not (RoutingCompiler.DirectGroup or RoutingCompiler.ProxyGroup))
                    try { if (!string.IsNullOrEmpty(group.Now)) await client.SelectAsync(group.Name, group.Now); } catch { /* 切订阅后组可能消失 */ }
            }
        }
        finally { _gate.Release(); }
    }

    private static async Task AtomicWriteAsync(string path, string content)
    {
        var temp = path + ".clashdock.tmp";
        await File.WriteAllTextAsync(temp, content);
        File.Move(temp, path, true);
    }
}
