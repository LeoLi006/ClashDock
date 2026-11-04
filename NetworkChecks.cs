using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;
namespace ClashDock;
internal static class NetworkChecks
{
    internal const string Original = "mode: global\nmixed-port: 1234\ntun:\n  enable: false\n  auto-route: false\n  device: FixtureTun\n  stack: mixed\nrules:\n- DOMAIN-SUFFIX,example.org,route-group\n- MATCH,DIRECT\nproxy-groups:\n- name: route-group\n  type: select\n  proxies: [Node-A, Node-B]\n";
    private sealed class Fake : HttpMessageHandler
    {
        public string Live = Original;
        public string Mode = "global";
        public JsonObject Tun = new() { ["enable"] = false, ["auto-route"] = false, ["device"] = "FixtureTun", ["stack"] = "mixed", ["dns-hijack"] = new JsonArray("any:53") };
        public string Choice = "Node-B";
        public int Writes, Reloads, RejectReloads, RejectPatches;
        public int FailRulesAtReload = -1;
        public bool MissingTun;
        public string RejectReason = "configure tun interface: Access is denied";
        public bool IgnoreTunEnable, FailRestore, ExtraRule;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                if (path == "/configs") { var config = new Dictionary<string, object> { ["mode"] = Mode, ["mixed-port"] = 7890 }; if (!MissingTun) config["tun"] = Tun; return Ok(config); }
                if (path == "/proxies")
                {
                    var proxies = new Dictionary<string, object> { ["route-group"] = new { type = "Selector", now = Choice, all = new[] { "Node-A", "Node-B" } }, ["Node-A"] = new { type = "Shadowsocks" }, ["Node-B"] = new { type = "Shadowsocks" } };
                    foreach (var group in RoutingCompiler.Groups(Rules(Live).Select(r => r.Raw).ToList(), "route-group"))
                        proxies[(string)group["name"]] = new { type = "Selector", now = ((string[])group["proxies"])[0], all = (string[])group["proxies"] };
                    return Ok(new { proxies });
                }
                if (path == "/rules")
                {
                    if (Reloads == FailRulesAtReload) { FailRulesAtReload = -1; throw new HttpRequestException("fixture verification read failed"); }
                    var rules = Rules(Live).Select(r => new { type = r.Type, payload = r.Payload, proxy = r.Proxy }).ToList();
                    if (ExtraRule) rules.Insert(0, new { type = "DomainSuffix", payload = "external.example", proxy = "DIRECT" });
                    return Ok(new { rules });
                }
            }
            Writes++;
            if (request.Method == HttpMethod.Put && path.StartsWith("/proxies/")) { Choice = (await Body(request))["name"]!.GetValue<string>(); return Ok(new { }); }
            if (request.Method == HttpMethod.Patch)
            {
                if (RejectPatches-- > 0 || FailRestore) return Error(RejectReason);
                var body = await Body(request);
                if (body["mode"] != null) Mode = body["mode"]!.GetValue<string>();
                if (body["tun"] is JsonObject tun)
                    foreach (var property in tun) if (!(IgnoreTunEnable && property.Key == "enable" && property.Value?.ToString() == "true")) Tun[property.Key] = property.Value?.DeepClone();
                return Ok(new { });
            }
            if (request.Method == HttpMethod.Put && path == "/configs")
            {
                Reloads++;
                if (RejectReloads-- > 0 || FailRestore) return Error("fixture rejected reload");
                Live = (await Body(request))["payload"]!.GetValue<string>();
                var map = RoutingCompiler.ReadYaml(Live); Mode = map.Children[new YamlScalarNode("mode")].ToString();
                if (map.Children.TryGetValue(new YamlScalarNode("tun"), out var raw) && raw is YamlMappingNode tun)
                    foreach (var property in tun.Children) Tun[property.Key.ToString()] = ToJson(property.Value);
                Choice = "Node-A"; return Ok(new { });
            }
            return Ok(new { });
        }
        static JsonNode? ToJson(YamlNode value) => value switch
        {
            YamlSequenceNode seq => new JsonArray(seq.Children.Select(ToJson).ToArray()),
            YamlMappingNode map => new JsonObject(map.Children.Select(p => new KeyValuePair<string, JsonNode?>(p.Key.ToString(), ToJson(p.Value)))),
            _ => value.ToString() is "true" or "false" ? JsonValue.Create(value.ToString() == "true") : JsonValue.Create(value.ToString())
        };
        static async Task<JsonObject> Body(HttpRequestMessage r) => JsonNode.Parse(await r.Content!.ReadAsStringAsync())!.AsObject();
        static HttpResponseMessage Ok(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
        static HttpResponseMessage Error(string message) => new(HttpStatusCode.BadRequest) { Content = new StringContent(JsonSerializer.Serialize(new { message })) };
    }
    private sealed record Rule(string Raw, string Type, string Payload, string Proxy);
    private static Rule[] Rules(string yaml) => ((YamlSequenceNode)RoutingCompiler.ReadYaml(yaml).Children[new YamlScalarNode("rules")]).Children.Select(n =>
    {
        string raw = n.ToString(); var parts = raw.Split(','); string type = parts[0] switch { "DOMAIN-SUFFIX" => "DomainSuffix", "PROCESS-PATH" => "ProcessPath", "PROCESS-NAME" => "ProcessName", "MATCH" => "Match", var t => t };
        return new Rule(raw, type, parts[0] == "MATCH" ? "" : string.Join(",", parts.Skip(1).SkipLast(1)), parts[^1]);
    }).ToArray();
    internal static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var result = new Dictionary<string, bool>();
        void Check(string name, bool value) { result[name] = value; if (!value) throw new InvalidOperationException(name); }
        async Task RunAsync()
        {
            var automatic = new RoutingSettings { DefaultBehavior = "未单独设置的应用全部直连", Applications = [new() { Target = @"C:\Apps\Auto.exe", Mode = "自动" }, new() { Target = @"C:\Apps\Proxy.exe", Mode = "代理" }], Proxy = "route-group" };
            var generated = RoutingCompiler.Rules(automatic);
            Check("explicit_auto_excluded_from_default_direct", generated[^1] == @"NOT,((PROCESS-PATH,C:\Apps\Auto.exe)),__ClashDock_DEFAULT_DIRECT");
            Check("explicit_proxy_precedes_default", generated[0].StartsWith("PROCESS-PATH,") && generated[0].EndsWith(RoutingCompiler.ProxyGroup));
            automatic.Applications.Add(new() { Target = "second.exe", Mode = "自动" });
            Check("multiple_auto_exclusions", RoutingCompiler.Rules(automatic)[^1] == @"NOT,((OR,((PROCESS-PATH,C:\Apps\Auto.exe),(PROCESS-NAME,second.exe)))),__ClashDock_DEFAULT_DIRECT");
            var runtime = RoutingCompiler.Runtime(Original, generated, "route-group");
            var values = Rules(runtime).Select(r => r.Raw).ToArray();
            Check("subscription_rules_retained_in_order", values.TakeLast(2).SequenceEqual(Rules(Original).Select(r => r.Raw)));
            var cleared = RoutingCompiler.Runtime(runtime, [], "");
            Check("remove_default_restores_subscription", Rules(cleared).Select(r => r.Raw).SequenceEqual(Rules(Original).Select(r => r.Raw)) && !cleared.Contains(RoutingCompiler.DefaultGroup));
            const string parserBase = "mode: rule\nmixed-port: 0\nport: 0\nsocks-port: 0\ntun:\n  enable: false\ndns:\n  enable: false\nproxies: []\nproxy-groups:\n- name: route-group\n  type: select\n  proxies: [DIRECT]\nrules:\n- DOMAIN-SUFFIX,example.org,route-group\n- MATCH,DIRECT\n";
            File.WriteAllText(Path.Combine(directory, "parser-default.yaml"), RoutingCompiler.Runtime(parserBase, RoutingCompiler.Rules(automatic), "route-group"));
            automatic.Applications[0].Target = @"C:\Program Files (x86)\Test\Auto.exe";
            File.WriteAllText(Path.Combine(directory, "parser-parentheses.yaml"), RoutingCompiler.Runtime(parserBase, RoutingCompiler.Rules(automatic), "route-group"));
            Check("script_idempotent_with_default", RoutingCompiler.Script(RoutingCompiler.Script("function main(c){return c;}", generated, "route-group"), generated, "route-group") == RoutingCompiler.Script("function main(c){return c;}", generated, "route-group"));
            var fake = new Fake(); var client = new MihomoClient("127.0.0.1", 1, "", fake);
            var tun = new TunControl(client, s => s.Enabled, () => Task.CompletedTask);
            var message = await tun.SetAsync(true);
            Check("tun_enable_verified_with_adapter", fake.Mode == "rule" && fake.Tun["enable"]!.GetValue<bool>() && message.Contains("已确认"));
            Check("tun_other_fields_preserved", fake.Tun["stack"]!.ToString() == "mixed" && fake.Tun["dns-hijack"] is JsonArray);
            await tun.SetAsync(false); Check("tun_disable_verified", !fake.Tun["enable"]!.GetValue<bool>());
            fake = new Fake { IgnoreTunEnable = true }; client = new MihomoClient("127.0.0.1", 1, "", fake);
            try { await new TunControl(client, _ => true, () => Task.CompletedTask).SetAsync(true); } catch (InvalidOperationException e) { Check("ignored_enable_not_success", e.Message.Contains("已恢复并核验")); }
            fake = new Fake(); client = new MihomoClient("127.0.0.1", 1, "", fake);
            message = ""; try { await new TunControl(client, _ => false, () => Task.CompletedTask).SetAsync(true); } catch (InvalidOperationException e) { message = e.Message; }
            Check("flag_without_adapter_not_success", message.Contains("网卡") && message.Contains("已恢复并核验") && fake.Mode == "global" && !fake.Tun["enable"]!.GetValue<bool>());
            fake = new Fake { RejectPatches = 1 }; client = new MihomoClient("127.0.0.1", 1, "", fake);
            try { await new TunControl(client, _ => true, () => Task.CompletedTask).SetAsync(true); } catch (InvalidOperationException e) { message = e.Message; }
            Check("permission_reason_preserved", message.Contains("Access is denied") && message.Contains("已恢复并核验"));
            fake = new Fake { MissingTun = true }; client = new MihomoClient("127.0.0.1", 1, "", fake);
            try { await new TunControl(client).SetAsync(true); } catch (InvalidOperationException e) { message = e.Message; }
            Check("missing_tun_state_no_mutation", fake.Writes == 0 && message.Contains("未执行"));
            fake = new Fake { RejectPatches = 1, RejectReason = "fixture-secret denied" }; client = new MihomoClient("127.0.0.1", 1, "fixture-secret", fake);
            try { await new TunControl(client).SetAsync(true); } catch (InvalidOperationException e) { message = e.Message; }
            Check("controller_secret_redacted_from_errors", !message.Contains("fixture-secret") && message.Contains("已隐藏"));
            fake = new Fake { FailRestore = true }; client = new MihomoClient("127.0.0.1", 1, "", fake);
            try { await new TunControl(client, _ => false, () => Task.CompletedTask).SetAsync(true); } catch (InvalidOperationException e) { message = e.Message; }
            Check("failed_rollback_not_reported_restored", message.Contains("恢复未确认"));
            var temp = Path.Combine(directory, "isolated-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(temp, "profiles"));
            string script = Path.Combine(temp, "profiles", "Script.js");
            File.WriteAllText(script, "function main(c) { return c; }");
            File.WriteAllText(Path.Combine(temp, "profiles.yaml"), "items:\n- uid: Script\n  file: Script.js\n"); File.WriteAllText(Path.Combine(temp, "clash-verge.yaml"), Original);
            fake = new Fake(); client = new MihomoClient("127.0.0.1", 1, "", fake);
            var settings = new Settings { Routing = new RoutingSettings { VergeDirectory = temp } }; int saves = 0;
            var service = new RoutingService(client, settings, () => saves++);
            var draft = new RoutingSettings { VergeDirectory = temp, Proxy = "route-group", DefaultBehavior = automatic.DefaultBehavior, Applications = automatic.Applications };
            await service.ApplyAsync(draft);
            var signature = await client.GetRuleSequenceAsync();
            Check("managed_rule_order_verified", !RoutingCompiler.RulesConfirmed(draft, signature.Reverse().ToArray()));
            Check("default_auto_exclusions_verified", !RoutingCompiler.RulesConfirmed(draft, signature.Select(x => x.Replace(@"C:\Program Files (x86)\Test\Auto.exe", "different.exe")).ToArray()));
            Check("save_verifies_and_persists_once", saves == 1 && settings.Routing == draft && fake.Mode == "rule");
            Check("save_preserves_tun_and_selector_choice", !fake.Tun["enable"]!.GetValue<bool>() && fake.Choice == "Node-B");
            Check("generated_file_not_overwritten", File.ReadAllText(Path.Combine(temp, "clash-verge.yaml")) == Original);
            Check("live_ports_preserved_over_stale_generated_file", RoutingCompiler.ReadYaml(fake.Live).Children[new YamlScalarNode("mixed-port")].ToString() == "7890");
            var installedScript = File.ReadAllText(script); var installedRuntime = fake.Live;
            fake.FailRulesAtReload = fake.Reloads + 1;
            try { await service.ApplyAsync(new RoutingSettings { VergeDirectory = temp }); } catch (InvalidOperationException e) { message = e.Message; }
            Check("post_apply_query_failure_restores_original", message.Contains("verification read failed") && message.Contains("已恢复并核验") && settings.Routing == draft && File.ReadAllText(script) == installedScript);
            fake.RejectReloads = 1;
            try { await service.ApplyAsync(new RoutingSettings { VergeDirectory = temp }); } catch (InvalidOperationException e) { message = e.Message; }
            Check("failed_apply_restores_exact_rule_sequence", Rules(installedRuntime).Select(r => r.Raw).SequenceEqual(Rules(fake.Live).Select(r => r.Raw)));
            Check("failed_apply_retains_settings_and_script", settings.Routing == draft && saves == 1 && File.ReadAllText(script) == installedScript && message.Contains("已恢复并核验"));
            service = new RoutingService(client, settings, () => throw new IOException("fixture disk full"));
            try { await service.ApplyAsync(new RoutingSettings { VergeDirectory = temp }); } catch (InvalidOperationException e) { message = e.Message; }
            Check("save_failure_rolls_back_network_and_draft", message.Contains("disk full") && message.Contains("已恢复并核验") && settings.Routing == draft && File.ReadAllText(script) == installedScript);
            fake.ExtraRule = true; int writes = fake.Writes;
            try { await service.ApplyAsync(new RoutingSettings { VergeDirectory = temp }); } catch (InvalidOperationException e) { message = e.Message; }
            Check("external_rules_refuse_overwrite", fake.Writes == writes && message.Contains("不一致")); fake.ExtraRule = false;
            fake.FailRestore = true;
            try { await service.ApplyAsync(new RoutingSettings { VergeDirectory = temp }); } catch (InvalidOperationException e) { message = e.Message; }
            Check("routing_rollback_uncertain_is_explicit", message.Contains("恢复未确认"));
            Check("preview_lists_changes_without_execution", NetworkConfirmation.Preview(new(), draft, await client.GetNetworkAsync()).Contains("未单独设置的应用全部直连"));
            // Drive the actual confirmation path, auto-cancelling only the isolated dialog.
            fake = new Fake(); client = new MihomoClient("127.0.0.1", 1, "", fake);
            var uiSettings = new Settings();
            using var ui = new RoutingForm(client, uiSettings, preview: true); ui.Show(); Application.DoEvents();
            typeof(RoutingForm).GetField("_preview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(ui, false);
            using var cancelDialog = new System.Windows.Forms.Timer { Interval = 30 };
            cancelDialog.Tick += (_, _) => { foreach (var dialog in Application.OpenForms.Cast<Form>().Where(f => f.Text == "确认网络改动").ToArray()) dialog.DialogResult = DialogResult.No; };
            cancelDialog.Start();
            async Task Invoke(string method) => await (Task)typeof(RoutingForm).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(ui, null)!;
            await Invoke("ApplyAsync"); Check("cancel_apply_confirmation_performs_no_network_write", fake.Writes == 0);
            await Invoke("ChangeTunAsync"); Check("cancel_tun_confirmation_performs_no_network_write", fake.Writes == 0);
            cancelDialog.Stop(); ui.Hide();
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        try { RunAsync().GetAwaiter().GetResult(); }
        catch (Exception e) { File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { Checks = result, Error = e.Message }, new JsonSerializerOptions { WriteIndented = true })); Environment.ExitCode = 1; }
    }
}
