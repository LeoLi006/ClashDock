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
    public string DefaultBehavior { get; set; } = "沿用订阅规则";
    public List<RouteEntry> Applications { get; set; } = new();
    public List<RouteEntry> Websites { get; set; } = new();
    public List<string> AppliedRules { get; set; } = new();
}

/// <summary>生成规则，并通过 Verge 全局脚本持久化。没有虚拟网卡或流量劫持逻辑。</summary>
public static class RoutingCompiler
{
    public const string DirectGroup = "__ClashDock_DIRECT";
    public const string ProxyGroup = "__ClashDock_PROXY";
    public const string DefaultGroup = "__ClashDock_DEFAULT_DIRECT";
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
        if (settings.DefaultBehavior is not ("沿用订阅规则" or "未单独设置的应用全部直连")) throw new ArgumentException("未知默认行为。");
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
        if (settings.DefaultBehavior == "未单独设置的应用全部直连")
        {
            var automatic = settings.Applications.Where(x => x.Mode == "自动").Select(x => "(" + (x.Target.Contains('\\') || x.Target.Contains('/') ? "PROCESS-PATH," : "PROCESS-NAME,") + x.Target.Trim() + ")").ToArray();
            // A default override precedes the intact subscription, but excludes explicitly automatic applications.
            string fallback = automatic.Length == 0 ? "MATCH," : automatic.Length == 1 ? "NOT,(" + automatic[0] + ")," : "NOT,((OR,(" + string.Join(",", automatic) + "))),";
            result.Add(fallback + DefaultGroup);
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

    internal static bool RulesConfirmed(RoutingSettings settings, string[] live)
    {
        var expected = Rules(settings);
        bool Managed(string key) => new[] { DirectGroup, ProxyGroup, DefaultGroup }.Any(g => key.EndsWith("|" + g, StringComparison.Ordinal));
        if (live.Count(Managed) != expected.Count || live.Length < expected.Count) return false;
        for (int i = 0; i < expected.Count; i++)
        {
            var api = live[i].Split('|'); var parts = expected[i].Split(',');
            if (api.Length != 3 || api[2] != parts[^1]) return false;
            if (parts[^1] == DefaultGroup)
            {
                if (!api[0].Replace("-", "").Equals(parts[0], StringComparison.OrdinalIgnoreCase)) return false;
                if (parts[0] == "NOT" && settings.Applications.Where(a => a.Mode == "自动").Any(a => !api[1].Contains(a.Target.Trim(), StringComparison.OrdinalIgnoreCase))) return false;
            }
            else if (!api[0].Replace("-", "").Equals(parts[0].Replace("-", ""), StringComparison.OrdinalIgnoreCase) || !api[1].Equals(parts[1], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public static string StripScript(string source)
    {
        var start = source.IndexOf(Begin, StringComparison.Ordinal);
        if (start < 0) { if (source.Contains(End, StringComparison.Ordinal)) throw new InvalidOperationException("ClashDock 脚本结束标记孤立，未覆盖用户脚本。请检查全局扩展脚本。"); return source; }
        var end = source.IndexOf(End, start, StringComparison.Ordinal);
        if (end < 0 || source.IndexOf(Begin, start + Begin.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("ClashDock 脚本标记损坏，请先恢复脚本备份。");
        return source[..start] + source[(end + End.Length)..];
    }

    public static string Script(string source, List<string> rules, string proxy)
    {
        var original = StripScript(source);
        if (original.Contains("__clashDockMain", StringComparison.Ordinal)) throw new InvalidOperationException("用户脚本与 ClashDock 包装变量重名，未覆盖脚本。请重命名用户脚本变量后重试。");
        if (rules.Count == 0) return original;
        // 在用户 main 之后包装，不改写用户已有脚本。
        return original.TrimEnd() + "\n\n" + Begin + "\n" +
            "var __clashDockMain = main;\n" +
            "main = function(config, profileName) {\n" +
            "  config = __clashDockMain(config, profileName);\n" +
            "  if (!config || typeof config !== 'object') throw new Error('Invalid original main result');\n" +
            "  config['proxy-groups'] = (config['proxy-groups'] || []).filter(function(g) { return g.name !== '" + DirectGroup + "' && g.name !== '" + ProxyGroup + "' && g.name !== '" + DefaultGroup + "'; }).concat(" + JsonSerializer.Serialize(Groups(rules, proxy)) + ");\n" +
            "  var dockGroup = config['proxy-groups'].find(function(g) { return g.name === '__ClashDock_PROXY'; });\n" +
            "  if (dockGroup) { var candidates = (config.proxies || []).concat(config['proxy-groups'] || []).map(function(p) { return p.name; }).filter(function(n) { return n && n !== 'GLOBAL' && n.indexOf('__ClashDock_') !== 0; }); dockGroup.proxies = Array.from(new Set(dockGroup.proxies.concat(candidates))); }\n" +
            "  config.rules = " + JsonSerializer.Serialize(rules) + ".concat((config.rules || []).filter(function(r) { return !/,__ClashDock_(DIRECT|PROXY|DEFAULT_DIRECT)$/.test(r); }));\n" +
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
        foreach (var (name, target) in new[] { (DirectGroup, "DIRECT"), (ProxyGroup, proxy), (DefaultGroup, "DIRECT") })
            if (rules.Any(r => r.EndsWith("," + name, StringComparison.Ordinal)))
                groups.Add(new() { ["name"] = name, ["type"] = "select", ["proxies"] = new[] { target } });
        return groups;
    }

    public static string Runtime(string source, List<string> rules, string proxy, IEnumerable<string>? available = null)
    {
        var map = ReadYaml(source);
        var key = new YamlScalarNode("rules");
        var existing = map.Children.TryGetValue(key, out var node) && node is YamlSequenceNode seq
            ? seq.Children.OfType<YamlScalarNode>().Select(n => n.Value ?? "").ToList()
            : throw new InvalidOperationException("运行配置缺少规则列表。");
        var replacement = new YamlSequenceNode();
        foreach (var rule in rules.Concat(existing.Where(r => !r.EndsWith("," + DirectGroup, StringComparison.Ordinal) && !r.EndsWith("," + ProxyGroup, StringComparison.Ordinal) && !r.EndsWith("," + DefaultGroup, StringComparison.Ordinal))))
            replacement.Add(new YamlScalarNode(rule));
        map.Children[key] = replacement;
        var groupKey = new YamlScalarNode("proxy-groups");
        var groupList = map.Children.TryGetValue(groupKey, out var groupsNode) && groupsNode is YamlSequenceNode existingGroups
            ? existingGroups : new YamlSequenceNode();
        foreach (var group in groupList.Children.OfType<YamlMappingNode>().Where(g =>
            g.Children.TryGetValue(new YamlScalarNode("name"), out var name) && name.ToString() is DirectGroup or ProxyGroup or DefaultGroup).ToList())
            groupList.Children.Remove(group);
        var options = groupList.Children.OfType<YamlMappingNode>().Where(g => g.Children.ContainsKey(new YamlScalarNode("name"))).Select(g => g.Children[new YamlScalarNode("name")].ToString()).ToList();
        if (map.Children.TryGetValue(new YamlScalarNode("proxies"), out var proxyNodes) && proxyNodes is YamlSequenceNode proxyList)
            options.AddRange(proxyList.Children.OfType<YamlMappingNode>().Where(g => g.Children.ContainsKey(new YamlScalarNode("name"))).Select(g => g.Children[new YamlScalarNode("name")].ToString()));
        foreach (var group in Groups(rules, proxy))
        {
            if ((string)group["name"] == ProxyGroup) group["proxies"] = new[] { proxy }.Concat(options).Concat(available ?? []).Where(n => n != "GLOBAL" && !n.StartsWith("__ClashDock_")).Distinct().ToArray();
            groupList.Add(new YamlMappingNode
            {
                { "name", (string)group["name"] }, { "type", "select" },
                { "proxies", new YamlSequenceNode(((string[])group["proxies"]).Select(p => (YamlNode)new YamlScalarNode(p))) }
            });
        }
        map.Children[groupKey] = groupList;
        map.Children[new YamlScalarNode("mode")] = new YamlScalarNode("rule");
        map.Children[new YamlScalarNode("find-process-mode")] = new YamlScalarNode("always");
        using var writer = new StringWriter();
        new YamlStream(new YamlDocument(map)).Save(writer, false);
        return writer.ToString();
    }
}

public sealed class RoutingService(MihomoClient client, Settings settings, Action? save = null)
{
    private readonly SemaphoreSlim _gate = NetworkGate.Gate;

    public async Task ApplyAsync(RoutingSettings draft)
    {
        await _gate.WaitAsync();
        try
        {
            if (settings.Host is not ("localhost" or "127.0.0.1" or "::1" or "[::1]"))
                throw new InvalidOperationException("分流配置仅支持本机 Verge 控制器。");
            var rules = RoutingCompiler.Rules(draft);
            var networkBefore = await client.GetNetworkAsync();
            var orderedRulesBefore = await client.GetRuleSequenceAsync();
            if (rules.Count > 0 && networkBefore.Mode != "rule")
                throw new RoutingFailure("mode_bypass", "当前不是规则模式，未应用或自动切换；请在 Verge 核对模式后重试。");
            if (draft.Applications.Any(e => e.Mode == "代理") || draft.Websites.Any(e => e.Mode == "代理"))
            {
                var names = await client.GetProxyNamesAsync();
                if (!names.Contains(draft.Proxy)) throw new RoutingFailure("target_missing", "代理目标已不存在，请重新选择节点或策略组。");
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
            if (!SubscriptionMatches(originalRuntime, orderedRulesBefore))
                throw new RoutingFailure("subscription_mismatch", "当前内核的非托管规则与 Verge 生成配置不一致，未覆盖网络配置。请先在 Verge 重新生成／应用当前订阅，再重试。");
            var updatedScript = RoutingCompiler.Script(originalScript, rules, draft.Proxy);
            var updatedRuntime = PreserveNetwork(RoutingCompiler.Runtime(originalRuntime, rules, draft.Proxy, await client.GetProxyNamesAsync()), networkBefore, preserveMode: rules.Count == 0);
            var selections = await client.GetSelectorGroupsAsync();
            var previousMode = networkBefore.Mode;
            var originalSettings = settings.Routing;
            // 备份仅包含全局脚本，不复制订阅；仅在用户点击应用时修改。
            var backup = scriptPath + ".clashdock-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8] + ".bak";
            File.Copy(scriptPath, backup, false);
            try { foreach (var old in Directory.EnumerateFiles(Path.GetDirectoryName(scriptPath)!, Path.GetFileName(scriptPath) + ".clashdock-*.bak").OrderByDescending(File.GetLastWriteTimeUtc).Skip(5)) File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (await File.ReadAllTextAsync(scriptPath) != originalScript)
                throw new InvalidOperationException("Verge 脚本被其他程序修改，请重试。");
            await AtomicWriteAsync(scriptPath, updatedScript);
            try
            {
                bool sameRules = RoutingCompiler.Rules(originalSettings).SequenceEqual(rules) && RoutingCompiler.RulesConfirmed(originalSettings, orderedRulesBefore) && networkBefore.Mode == "rule";
                var managed = selections.FirstOrDefault(g => g.Name == RoutingCompiler.ProxyGroup);
                bool selectorOnly = sameRules && draft.Proxy != originalSettings.Proxy && managed != null && managed.Options.Contains(draft.Proxy);
                if (selectorOnly) {
                    await client.SelectAsync(RoutingCompiler.ProxyGroup, draft.Proxy);
                    if (!(await client.GetSelectorGroupsAsync()).Any(g => g.Name == RoutingCompiler.ProxyGroup && g.Now == draft.Proxy)) throw new InvalidOperationException("代理目标切换未确认。");
                } else await client.ReloadAsync(updatedRuntime);
                if (!RoutingCompiler.RulesConfirmed(draft, await client.GetRuleSequenceAsync()))
                    throw new InvalidOperationException("托管规则内容、顺序或默认行为未被内核确认。");
                var networkAfter = await client.GetNetworkAsync();
                if (networkAfter.Mode != (rules.Count == 0 ? networkBefore.Mode : "rule") || !SameTun(networkBefore, networkAfter)) throw new InvalidOperationException("规则模式或原 TUN 状态未被内核确认。");
                if (!selectorOnly) await RestoreSelections(client, selections);
                if (rules.Any(r => r.EndsWith("," + RoutingCompiler.ProxyGroup, StringComparison.Ordinal))) {
                    await client.SelectAsync(RoutingCompiler.ProxyGroup, draft.Proxy);
                    if (!(await client.GetSelectorGroupsAsync()).Any(g => g.Name == RoutingCompiler.ProxyGroup && g.Now == draft.Proxy)) throw new InvalidOperationException("代理目标读回不匹配。");
                }
                draft.AppliedRules = rules;
                settings.Routing = draft;
                (save ?? settings.SaveChecked)();
            }
            catch (Exception error)
            {
                settings.Routing = originalSettings;
                bool scriptRestored = false, runtimeRestored = false;
                try
                {
                    var current = await File.ReadAllTextAsync(scriptPath);
                    if (current == updatedScript) await AtomicWriteAsync(scriptPath, originalScript);
                    scriptRestored = await File.ReadAllTextAsync(scriptPath) == originalScript;
                }
                catch { }
                try
                {
                    var rollback = PreserveNetwork(RoutingCompiler.Runtime(originalRuntime, originalSettings.AppliedRules, originalSettings.Proxy), networkBefore, preserveMode: true);
                    await client.ReloadAsync(rollback);
                    var restoreConfig = new Dictionary<string, object> { ["mode"] = previousMode };
                    if (networkBefore.HasTun) restoreConfig["tun"] = networkBefore.Tun;
                    await client.PatchConfigAsync(restoreConfig);
                    await RestoreSelections(client, selections);
                    var state = await client.GetNetworkAsync();
                    runtimeRestored = state.Mode == previousMode && SameTun(networkBefore, state) && orderedRulesBefore.SequenceEqual(await client.GetRuleSequenceAsync());
                }
                catch { }
                throw new InvalidOperationException(error.Message + (scriptRestored && runtimeRestored
                    ? "\n应用失败：原脚本、规则顺序、策略组及 TUN／模式已恢复并核验；草稿保留。"
                    : $"\n应用失败：原脚本{(scriptRestored ? "已恢复" : "恢复未确认")}，运行配置{(runtimeRestored ? "已核验" : "恢复未确认")}。请在 Verge 检查后重试；草稿保留。"));
            }
        }
        finally { _gate.Release(); }
    }

    internal static bool SubscriptionMatches(string yaml, string[] live)
    {
        bool Managed(string proxy) => proxy is RoutingCompiler.DirectGroup or RoutingCompiler.ProxyGroup or RoutingCompiler.DefaultGroup;
        string Normalize(string type, string payload, string proxy)
        {
            var normalized = type.Replace("-", "").ToUpperInvariant();
            // Mihomo reports both IPv4 and IPv6 CIDR rules as IPCIDR in /rules.
            // Only accept the alias for a valid IPv6 network; keep payload and target exact.
            if (normalized == "IPCIDR6" && payload.Split('/') is [var address, var prefix]
                && System.Net.IPAddress.TryParse(address, out var ip)
                && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                && int.TryParse(prefix, out var bits) && bits >= 0 && bits <= 128)
                normalized = "IPCIDR";
            if (normalized == "GEOIP" && payload.Length == 2 && payload.All(char.IsAsciiLetter))
                payload = payload.ToUpperInvariant();
            return normalized + "|" + payload + "|" + proxy;
        }
        var map = RoutingCompiler.ReadYaml(yaml);
        var source = ((YamlSequenceNode)map.Children[new YamlScalarNode("rules")]).Children.Select(node =>
        {
            string rule = node.ToString(); var fields = new List<string>(); int depth = 0, start = 0;
            for (int i = 0; i < rule.Length; i++) { if (rule[i] == '(') depth++; else if (rule[i] == ')') depth--; else if (rule[i] == ',' && depth == 0) { fields.Add(rule[start..i].Trim()); start = i + 1; } }
            fields.Add(rule[start..].Trim());
            bool match = fields[0] == "MATCH";
            if (fields.Count < (match ? 2 : 3)) throw new InvalidOperationException("无法核验原订阅规则，未覆盖配置。");
            return (Type: fields[0], Payload: match ? "" : fields[1], Proxy: match ? fields[1] : fields[2]);
        }).Where(r => !Managed(r.Proxy)).Select(r => Normalize(r.Type, r.Payload, r.Proxy));
        var current = live.Select(r => r.Split('|')).Where(r => r.Length == 3 && !Managed(r[2])).Select(r => Normalize(r[0], r[1], r[2]));
        return source.SequenceEqual(current);
    }

    private static bool SameTun(NetworkSnapshot a, NetworkSnapshot b) => a.HasTun == b.HasTun && System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(a.Tun.GetRawText()), System.Text.Json.Nodes.JsonNode.Parse(b.Tun.GetRawText()));
    private static string PreserveNetwork(string yaml, NetworkSnapshot network, bool preserveMode = false)
    {
        var map = RoutingCompiler.ReadYaml(yaml);
        // Keep live runtime ports and TUN settings rather than stale values from the generated file.
        foreach (var property in network.Config.EnumerateObject().Where(p => p.Name is "tun" or "port" or "socks-port" or "mixed-port" or "redir-port" or "tproxy-port" or "allow-lan" or "bind-address" or "ipv6" or "interface-name" or "routing-mark" or "log-level" or "tcp-concurrent" or "unified-delay"))
            map.Children[new YamlScalarNode(property.Name)] = new YamlStreamFromJson(property.Value).Node;
        if (preserveMode) map.Children[new YamlScalarNode("mode")] = new YamlScalarNode(network.Mode);
        using var writer = new StringWriter(); new YamlStream(new YamlDocument(map)).Save(writer, false); return writer.ToString();
    }
    private sealed class YamlStreamFromJson(JsonElement value)
    {
        public YamlNode Node { get; } = Convert(value);
        private static YamlNode Convert(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Object => new YamlMappingNode(value.EnumerateObject().Select(p => new KeyValuePair<YamlNode, YamlNode>(new YamlScalarNode(p.Name), Convert(p.Value)))),
            JsonValueKind.Array => new YamlSequenceNode(value.EnumerateArray().Select(Convert)),
            JsonValueKind.String => new YamlScalarNode(value.GetString()),
            JsonValueKind.Null => new YamlScalarNode("null"),
            _ => new YamlScalarNode(value.GetRawText())
        };
    }
    private static async Task RestoreSelections(MihomoClient client, List<ProxyGroup> before)
    {
        foreach (var group in before.Where(g => g.Name is not (RoutingCompiler.DirectGroup or RoutingCompiler.ProxyGroup or RoutingCompiler.DefaultGroup) && g.Now.Length > 0))
            await client.SelectAsync(group.Name, group.Now);
        var after = await client.GetSelectorGroupsAsync();
        if (before.Where(g => g.Name is not (RoutingCompiler.DirectGroup or RoutingCompiler.ProxyGroup or RoutingCompiler.DefaultGroup) && g.Now.Length > 0).Any(g => !after.Any(a => a.Name == g.Name && a.Now == g.Now)))
            throw new InvalidOperationException("策略组原选择恢复未确认。");
    }

    private static async Task AtomicWriteAsync(string path, string content)
    {
        var temp = path + ".clashdock.tmp";
        await File.WriteAllTextAsync(temp, content);
        File.Move(temp, path, true);
    }
}
