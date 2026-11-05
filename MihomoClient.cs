using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ClashDock;

/// <summary>Mihomo(Clash Verge) 外部控制器 REST API 的轻量客户端。</summary>
public sealed partial class MihomoClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _base;
    private readonly string _secret;
    public void Dispose() { Targets.Dispose(); _http.Dispose(); }
    public bool IsLocal { get; }
    public TargetCache Targets { get; }

    public MihomoClient(string host, int port, string secret, HttpMessageHandler? handler = null)
    {
        Targets = new TargetCache(this);
        IsLocal = host is "localhost" or "127.0.0.1" or "::1" or "[::1]";
        _secret = secret;
        _base = $"http://{host}:{port}";
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(20);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", secret);
        }
    }

    public async Task<NetworkSnapshot> GetNetworkAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"{_base}/configs", ct); await EnsureAsync(response);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("mode", out var mode) || mode.ValueKind != JsonValueKind.String || mode.GetString() is not ("rule" or "global" or "direct"))
            throw new IOException("控制器返回的网络模式无效，未认定运行正常。");
        return new NetworkSnapshot(doc.RootElement.Clone());
    }
    public async Task PatchConfigAsync(object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{_base}/configs") { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        using var response = await _http.SendAsync(request); await EnsureAsync(response);
    }
    private async Task EnsureAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string reason = response.StatusCode switch { System.Net.HttpStatusCode.Unauthorized => "控制器认证失败，请刷新本机连接配置", System.Net.HttpStatusCode.Forbidden => "控制器拒绝权限，请检查 Verge 服务模式", _ => "内核拒绝操作" };
        try { using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); if (doc.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String) reason += "：" + message.GetString(); } catch (JsonException) { }
        if (_secret.Length > 0) reason = reason.Replace(_secret, "[已隐藏]");
        throw new InvalidOperationException($"HTTP {(int)response.StatusCode} · " + reason[..Math.Min(500, reason.Length)]);
    }
    public async Task<string[]> GetRuleSequenceAsync(CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync($"{_base}/rules", ct));
        return doc.RootElement.GetProperty("rules").EnumerateArray().Select(r => $"{r.GetProperty("type").GetString()}|{r.GetProperty("payload").GetString()}|{r.GetProperty("proxy").GetString()}").ToArray();
    }

    /// <summary>连通性探测。</summary>
    public async Task<bool> PingAsync()
    {
        try
        {
            using var resp = await _http.GetAsync($"{_base}/version");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> GetModeAsync()
    {
        var json = await _http.GetStringAsync($"{_base}/configs");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("mode", out var m)
            ? m.GetString() ?? "unknown"
            : "unknown";
    }

    public async Task<bool> GetTunAsync()
    {
        var json = await _http.GetStringAsync($"{_base}/configs");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("tun", out var tun)
            && tun.TryGetProperty("enable", out var en)
            && en.ValueKind == JsonValueKind.True;
    }

    public async Task SetModeAsync(string mode)
    {
        var body = new StringContent(
            $"{{\"mode\":\"{mode}\"}}", Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(new HttpMethod("PATCH"), $"{_base}/configs")
        {
            Content = body
        };
        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>返回所有 Selector 类型策略组：组名、当前选中项、全部可选项。</summary>
    public async Task<List<ProxyGroup>> GetSelectorGroupsAsync()
    {
        var json = await _http.GetStringAsync($"{_base}/proxies");
        var result = new List<ProxyGroup>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("proxies", out var proxies))
            return result;

        foreach (var prop in proxies.EnumerateObject())
        {
            var v = prop.Value;
            var type = v.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (!string.Equals(type, "Selector", StringComparison.OrdinalIgnoreCase))
                continue;

            var group = new ProxyGroup
            {
                Name = prop.Name,
                Now = v.TryGetProperty("now", out var now) ? now.GetString() ?? "" : ""
            };
            if (v.TryGetProperty("all", out var all) && all.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in all.EnumerateArray())
                {
                    var option = item.GetString();
                    if (!string.IsNullOrEmpty(option))
                        group.Options.Add(option);
                }
            }
            result.Add(group);
        }
        return result;
    }

    public async Task SelectAsync(string group, string option)
    {
        var payload = JsonSerializer.Serialize(new { name = option });
        var body = new StringContent(payload, Encoding.UTF8, "application/json");
        var url = $"{_base}/proxies/{Uri.EscapeDataString(group)}";
        using var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = body };
        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }

    public async Task ReloadAsync(string yaml)
    {
        using var body = new StringContent(JsonSerializer.Serialize(new { payload = yaml }), Encoding.UTF8, "application/json");
        using var resp = await _http.PutAsync($"{_base}/configs?force=true", body);
        await EnsureAsync(resp);
    }

    public async Task<List<string>> GetProxyNamesAsync()
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync($"{_base}/proxies"));
        return doc.RootElement.GetProperty("proxies").EnumerateObject()
            .Where(p => p.Value.GetProperty("type").GetString() is not ("Direct" or "Reject" or "Pass" or "Compatible"))
            .Select(p => p.Name).Where(n => n != "GLOBAL" && n is not (RoutingCompiler.DirectGroup or RoutingCompiler.ProxyGroup or RoutingCompiler.DefaultGroup)).OrderBy(n => n).ToList();
    }

    public async Task<HashSet<string>> GetRuleKeysAsync()
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync($"{_base}/rules"));
        return doc.RootElement.GetProperty("rules").EnumerateArray()
            .Select(r => $"{r.GetProperty("type").GetString()}|{r.GetProperty("payload").GetString()}|{r.GetProperty("proxy").GetString()}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class ProxyGroup
{
    public string Name { get; set; } = "";
    public string Now { get; set; } = "";
    public List<string> Options { get; } = new();
}
