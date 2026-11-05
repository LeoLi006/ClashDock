using System.Text.Json;
namespace ClashDock;

// UI-thread owned. Intent is durable before the debounce; one worker owns network mutations.
public sealed class AutoRouting
{
    readonly RoutingService service;
    readonly Settings settings;
    readonly string intentPath;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 450 };
    Task worker = Task.CompletedTask;
    long revision;
    bool pending, stopped, unsaved;
    public RoutingSettings Desired { get; private set; }
    public string Status { get; private set; } = "尚未核验 · 初次加载不会修改网络";
    public long Revision => revision;
    public RoutingSettings Applied => Copy(settings.Routing);
    public bool AppliedPaused => settings.Paused;
    public string StateCode => unsaved ? "save_failed" : Busy || pending ? "applying" : Status.StartsWith("已生效") ? "applied" : Status.StartsWith("已暂停") ? "paused" : Status.Contains("覆盖") ? "overwritten" : Status.Contains("控制器未连接") ? "controller_offline" : Status.Contains("失败") || Status.Contains("待重试") ? "retry" : "unknown";
    public bool Busy => !worker.IsCompleted;
    public bool Paused { get; private set; }
    public event Action? Changed;
    public static RoutingSettings Copy(RoutingSettings value) => JsonSerializer.Deserialize<RoutingSettings>(JsonSerializer.Serialize(value))!;
    public AutoRouting(MihomoClient client, Settings settings, string? path = null, Action? save = null)
    {
        this.settings = settings; service = new(client, settings, save);
        intentPath = path ?? Path.Combine(AppContext.BaseDirectory, "routing-intent.json");
        Desired = Copy(settings.Paused && settings.ResumeRouting != null ? settings.ResumeRouting : settings.Routing); Paused = settings.Paused;
        try { if (File.Exists(intentPath)) { var saved = JsonSerializer.Deserialize<Intent>(File.ReadAllText(intentPath)); if (saved != null) { Desired = saved.Draft; Paused = saved.Paused; Status = "待重试 · 已恢复上次修改，请点击重试；尚未自动应用"; } } }
        catch { Status = "待重试 · 修改记录无法读取，请检查高级配置"; }
        timer.Tick += (_, _) => { timer.Stop(); Start(); };
    }
    sealed record Intent(RoutingSettings Draft, bool Paused);
    void Persist() { var tmp = intentPath + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(new Intent(Desired, Paused))); File.Move(tmp, intentPath, true); unsaved = false; }
    public void Update(RoutingSettings draft)
    {
        RoutingCompiler.Rules(draft); Desired = Copy(draft); revision++; pending = true;
        try { Persist(); Status = Paused ? "等待应用 · 暂停意图已保存" : "等待应用 · 修改已保存"; timer.Stop(); timer.Start(); }
        catch (Exception error) { pending = false; unsaved = true; Status = "保存失败（" + error.GetType().Name + "）· 修改仍在窗口中，请重试，暂未应用"; }
        Changed?.Invoke();
    }
    public void Retry() { try { Persist(); pending = true; revision++; Start(); } catch { unsaved = true; Status = "保存失败 · 请检查程序目录写入权限"; Changed?.Invoke(); } }
    public void TogglePause() { Paused = !Paused; Update(Desired); }
    public void RemoveAll() { Paused = false; Update(new RoutingSettings { VergeDirectory = Desired.VergeDirectory, Proxy = Desired.Proxy }); }
    public void Undo()
    {
        var path = intentPath + ".undo";
        if (!File.Exists(path)) throw new InvalidOperationException("没有可撤销的成功修改。");
        Update(JsonSerializer.Deserialize<RoutingSettings>(File.ReadAllText(path))!);
    }
    void Start() { if (Busy || !pending) return; worker = Run(); }
    async Task Run()
    {
        while (pending)
        {
            pending = false; long current = revision; var desired = Copy(Desired); bool pause = Paused;
            var previous = Copy(settings.Paused && settings.ResumeRouting != null ? settings.ResumeRouting : settings.Routing);
            bool previousPaused = settings.Paused; var previousResume = settings.ResumeRouting;
            Status = "正在应用…"; Changed?.Invoke();
            try
            {
                var applied = pause ? new RoutingSettings { VergeDirectory = desired.VergeDirectory, Proxy = desired.Proxy } : Copy(desired);
                settings.Paused = pause; settings.ResumeRouting = Copy(desired);
                bool saveOnly = pause && previousPaused && RoutingCompiler.Rules(settings.Routing).Count == 0;
                if (saveOnly) settings.SaveChecked();
                else await service.ApplyAsync(applied);
                try { File.WriteAllText(intentPath + ".undo.tmp", JsonSerializer.Serialize(previous)); File.Move(intentPath + ".undo.tmp", intentPath + ".undo", true); } catch { }
                if (current == revision) { Status = saveOnly ? "已保存 · 恢复意图已更新，暂停状态待核验" : pause ? "已暂停 · 仅撤下 ClashDock 规则" : "已生效 · 内核规则已确认；已有连接可能需重连"; try { File.Delete(intentPath); } catch { Status += "；恢复记录未清理，请检查目录权限"; } }
            }
            catch (Exception e) { settings.Paused = previousPaused; settings.ResumeRouting = previousResume; if (current == revision) Status = "待重试 · 应用失败：" + e.Message; }
            Changed?.Invoke();
        }
    }
    public async Task FlushAsync() { timer.Stop(); if (pending) Start(); await worker; if (unsaved || File.Exists(intentPath)) Persist(); }
    public async Task VerifyAsync(MihomoClient client)
    {
        if (stopped || Busy || pending || File.Exists(intentPath)) return;
        try
        {
            var network = await client.GetNetworkAsync(); var rules = await client.GetRuleSequenceAsync();
            if (stopped || Busy || pending) return;
            bool targetValid = !RoutingCompiler.Rules(settings.Routing).Any(r => r.EndsWith("," + RoutingCompiler.ProxyGroup, StringComparison.Ordinal)) || (await client.GetSelectorGroupsAsync()).Any(g => g.Name == RoutingCompiler.ProxyGroup && g.Now == settings.Routing.Proxy);
            if (stopped || Busy || pending) return;
            Status = targetValid && RoutingCompiler.RulesConfirmed(settings.Routing, rules) && (RoutingCompiler.Rules(settings.Routing).Count == 0 || network.Mode == "rule")
                ? (Paused ? "已暂停 · ClashDock 规则已撤下" : "已生效 · 内核规则已确认") : "配置被覆盖 · 请检查 Verge 后手动重试（不会争抢配置）";
        }
        catch { if (stopped) return; Status = "控制器未连接 · 保留修改，可连接后重试"; }
        Changed?.Invoke();
    }
    public void Stop() { if (stopped) return; stopped = true; Changed = null; timer.Stop(); timer.Dispose(); }
}
