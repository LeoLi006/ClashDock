using System.Runtime.InteropServices;
namespace ClashDock;
public sealed class TrayApp : ApplicationContext
{
    readonly Settings settings;
    readonly MihomoClient client;
    readonly AutoRouting engine;
    readonly NotifyIcon tray;
    readonly DesktopBridge bridge;
    CheckRoutingForm? checkForm;
    readonly Control dispatcher = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 12000 };
    readonly HotkeyWindow hotkey = new();
    RoutingForm? form;
    bool checking, exiting;
    public TrayApp(bool silent = false)
    {
        settings = Settings.Load(); ControllerDiscovery.Apply(settings);
        client = new(settings.Host, settings.Port, settings.Secret); engine = new(client, settings);
        dispatcher.CreateControl(); _ = dispatcher.Handle;
        if (SynchronizationContext.Current == null) SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        bridge = new DesktopBridge(dispatcher, new BridgeDispatcher(client, engine, domain => { if(exiting)return; checkForm?.Close(); checkForm = new CheckRoutingForm(client,engine,domain,false); checkForm.Show(); ShowWindow(checkForm.Handle,9); SetForegroundWindow(checkForm.Handle); }));
        tray = new() { Icon = SystemIcons.Application, Text = "ClashDock", Visible = true };
        tray.DoubleClick += (_, _) => Show(); hotkey.HotkeyPressed += (_, _) => Show();
        engine.Changed += UpdateMenu; UpdateMenu();
        timer.Tick += async (_, _) => { if (checking || exiting) return; checking = true; try { await engine.VerifyAsync(client); } finally { checking = false; } }; timer.Start();
        if (!silent) Show();
        _ = engine.VerifyAsync(client);
    }
    public void RequestOpen() { if (!dispatcher.IsDisposed) try { dispatcher.BeginInvoke((Action)Show); } catch (InvalidOperationException) { } }
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    void Show()
    {
        if (exiting) return;
        if (form == null || form.IsDisposed) form = new(client, settings, engine: engine);
        form.Show(); if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        ShowWindow(form.Handle, 9); form.BringToFront(); form.Activate(); SetForegroundWindow(form.Handle);
    }
    void UpdateMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.Body };
        menu.Items.Add("打开主界面", null, (_, _) => Show());
        menu.Items.Add(engine.Paused ? "恢复分流" : "暂停分流", null, (_, _) => engine.TogglePause());
        menu.Items.Add("退出（保留已生效规则）", null, async (_, _) => await ExitAsync());
        var old = tray.ContextMenuStrip; tray.ContextMenuStrip = menu; old?.Dispose();
        tray.Text = "ClashDock · " + engine.Status[..Math.Min(45, engine.Status.Length)].Replace("\n", " ");
    }
    async Task ExitAsync()
    {
        if (exiting) return; exiting = true;
        try { form?.CommitPendingEdit(); await engine.FlushAsync(); }
        catch { exiting = false; MessageBox.Show("最后的修改尚未保存，暂未退出。请在主界面重试。"); return; }
        await bridge.StopAsync(); checkForm?.Dispose(); timer.Stop(); timer.Dispose(); engine.Stop(); form?.Dispose(); hotkey.Dispose();
        tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); client.Dispose(); dispatcher.Dispose(); ExitThread();
    }
}
