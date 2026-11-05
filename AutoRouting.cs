using System.Text.Json;
namespace ClashDock;

// UI-thread owned. Intent is durable before the debounce; one worker owns network mutations.
public sealed class AutoRouting
{
    readonly RoutingService service;
    readonly Settings settings;
    readonly Action saveSettings;
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
    string phase = "unknown";
    public string FailureCode { get; private set; } = "";
    public string FailureMessage { get; private set; } = "";
    public bool IntentSaved => !unsaved;
    public string StateCode => unsaved ? "save_failed" : phase;
    void Set(string code, string text) { phase = code; Status = text; }
    public bool Busy => !worker.IsCompleted;
    public bool Paused { get; private set; }
    public event Action? Changed;
    public static RoutingSettings Copy(RoutingSettings value) => JsonSerializer.Deserialize<RoutingSettings>(JsonSerializer.Serialize(value))!;
    public AutoRouting(MihomoClient client, Settings settings, string? path = null, Action? save = null)
    {
        this.settings = settings; saveSettings = save ?? settings.SaveChecked; service = new(client, settings, save);
        intentPath = path ?? Path.Combine(AppContext.BaseDirectory, "routing-intent.json");
        Desired = Copy(settings.Paused && settings.ResumeRouting != null ? settings.ResumeRouting : settings.Routing); Paused = settings.Paused;
        try { if (File.Exists(intentPath)) { var saved = JsonSerializer.Deserialize<Intent>(File.ReadAllText(intentPath)); if (saved != null) { Desired = saved.Draft; Paused = saved.Paused; Set("saved", "已保存但未生效 · 已恢复上次修改，未自动应用"); } } }
        catch { unsaved = true; FailureCode = "save_failed"; FailureMessage = "保存记录无法读取 · 请检查高级配置"; Set("save_failed", FailureMessage); }
        timer.Tick += (_, _) => { timer.Stop(); Start(); };
    }
    sealed record Intent(RoutingSettings Draft, bool Paused);
    void Persist() { var tmp = intentPath + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(new Intent(Desired, Paused))); File.Move(tmp, intentPath, true); unsaved = false; }
    public void Update(RoutingSettings draft)
    {
        RoutingCompiler.Rules(draft); Desired = Copy(draft); revision++; pending = true;
        try { Persist(); FailureCode = FailureMessage = ""; Set("waiting", Paused ? "等待应用 · 暂停意图已保存" : "等待应用 · 修改已保存"); timer.Stop(); timer.Start(); }
        catch (Exception error) { pending = false; unsaved = true; FailureCode = "save_failed"; FailureMessage = "保存失败（" + error.GetType().Name + "），修改未持久化"; Set("save_failed", FailureMessage); }
        Changed?.Invoke();
    }
    public void Retry() { try { Persist(); FailureCode = FailureMessage = ""; pending = true; revision++; Set("waiting", "等待应用 · 将重新应用全部分流"); Start(); } catch { unsaved = true; FailureCode = "save_failed"; FailureMessage = "保存失败 · 请检查程序目录写入权限"; Set("save_failed", FailureMessage); Changed?.Invoke(); } }
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
            Set("applying", "正在应用全部分流…"); Changed?.Invoke();
            try
            {
                var applied = pause ? new RoutingSettings { VergeDirectory = desired.VergeDirectory, Proxy = desired.Proxy } : Copy(desired);
                settings.Paused = pause; settings.ResumeRouting = Copy(desired);
                bool saveOnly = pause && previousPaused && RoutingCompiler.Rules(settings.Routing).Count == 0;
                if (saveOnly) saveSettings();
                else await service.ApplyAsync(applied);
                try { File.WriteAllText(intentPath + ".undo.tmp", JsonSerializer.Serialize(previous)); File.Move(intentPath + ".undo.tmp", intentPath + ".undo", true); } catch { }
                if (current == revision) { Set(saveOnly ? "saved" : pause ? "paused" : "applied", saveOnly ? "已保存 · 恢复意图已更新，暂停状态待核验" : pause ? "已暂停 · 仅撤下 ClashDock 规则" : "规则已加载 · 内核规则已确认；未验证实际出口，已有连接可能需重连"); try { File.Delete(intentPath); } catch { Status += "；恢复记录未清理，请检查目录权限"; } }
            }
            catch (Exception e) { settings.Paused = previousPaused; settings.ResumeRouting = previousResume; if (current == revision) { FailureCode = RoutingFailure.CodeOf(e); FailureMessage = e.Message; Set(FailureCode == "controller_offline" ? "controller_offline" : "retry", "应用失败 · " + FailureMessage); } }
            Changed?.Invoke();
        }
    }
    public async Task FlushAsync() { timer.Stop(); if (pending) Start(); await worker; if (unsaved || File.Exists(intentPath)) Persist(); }
    public async Task VerifyAsync(MihomoClient client)
    {
        if (stopped || Busy || pending || File.Exists(intentPath)) return;
        try
        {
            long current = revision;
            var result = await new RoutingInspector(client, this).VerifyAsync("example.com", false, CancellationToken.None);
            if (stopped || Busy || pending || current != revision || File.Exists(intentPath)) return;
            Set(result.ProblemCode, result.Conclusion);
        }
        catch { if (stopped) return; Set("controller_offline", "控制器未连接 · 保留修改，可连接后重试"); }
        Changed?.Invoke();
    }
    public void Stop() { if (stopped) return; stopped = true; Changed = null; timer.Stop(); timer.Dispose(); }
}
