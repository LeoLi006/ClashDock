namespace ClashDock;

/// <summary>托盘主逻辑：状态刷新、模式切换、策略组一键切换、热键唤起。</summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly Settings _settings;
    private readonly MihomoClient _client;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly HotkeyWindow _hotkey;
    private PanelForm? _panel;
    private RoutingForm? _routingPanel;
    private bool _busy;

    public TrayApp()
    {
        _settings = Settings.Load();
        ControllerDiscovery.Apply(_settings);
        _client = new MihomoClient(_settings.Host, _settings.Port, _settings.Secret);

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "ClashDock 启动中…"
        };
        _tray.DoubleClick += (_, _) => ShowRouting();
        _tray.ContextMenuStrip = BuildMenu(null, null, false);

        _hotkey = new HotkeyWindow();
        _hotkey.HotkeyPressed += (_, _) => ShowRouting();

        _timer = new System.Windows.Forms.Timer { Interval = 4000 };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        if (await _client.PingAsync())
        {
            if (_settings.ForceRuleModeOnStart)
            {
                try
                {
                    var mode = await _client.GetModeAsync();
                    if (!string.Equals(mode, "rule", StringComparison.OrdinalIgnoreCase))
                        await _client.SetModeAsync("rule");
                }
                catch
                {
                    // 忽略
                }
            }
        }
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            if (!await _client.PingAsync())
            {
                _tray.Text = "ClashDock：未连接到 Clash";
                _tray.ContextMenuStrip = BuildMenu(null, null, false);
                return;
            }

            var mode = await _client.GetModeAsync();
            var groups = await _client.GetSelectorGroupsAsync();

            _tray.Text = $"ClashDock  [{mode}]  组:{groups.Count}";
            _tray.ContextMenuStrip = BuildMenu(mode, groups, true);
        }
        catch
        {
            _tray.Text = "ClashDock：读取失败";
        }
        finally
        {
            _busy = false;
        }
    }

    private ContextMenuStrip BuildMenu(
        string? mode, List<ProxyGroup>? groups, bool connected)
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            Font = Theme.Body
        };

        var header = new ToolStripMenuItem(
            connected ? $"已连接  模式: {mode}" : "未连接到 Clash (检查控制器/密钥)")
        {
            Enabled = false
        };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        // 模式切换
        var modeMenu = new ToolStripMenuItem("运行模式");
        foreach (var m in new[] { "rule", "global", "direct" })
        {
            var item = new ToolStripMenuItem(m)
            {
                Checked = string.Equals(mode, m, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += async (_, _) => await SafeSetModeAsync(m);
            modeMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(modeMenu);

        // 一键纠偏
        var fix = new ToolStripMenuItem("强制切回规则模式(rule)");
        fix.Click += async (_, _) => await SafeSetModeAsync("rule");
        menu.Items.Add(fix);

        // 场景预设：一键套用常用组合（切模式 + 批量切多个策略组）
        if (connected && _settings.Presets.Count > 0)
        {
            var presetRoot = new ToolStripMenuItem("场景预设(一键套用)");
            foreach (var p in _settings.Presets)
            {
                var captured = p;
                var item = new ToolStripMenuItem(captured.Name);
                item.Click += async (_, _) => await SafeApplyPresetAsync(captured);
                presetRoot.DropDownItems.Add(item);
            }
            presetRoot.DropDownItems.Add(new ToolStripSeparator());
            var save = new ToolStripMenuItem("保存当前状态为场景…");
            save.Click += async (_, _) => await SaveCurrentAsPresetAsync();
            presetRoot.DropDownItems.Add(save);
            menu.Items.Add(presetRoot);
        }

        menu.Items.Add(new ToolStripSeparator());

        // 策略组：每个组一个子菜单，点选即切换
        if (groups is { Count: > 0 })
        {
            var groupsRoot = new ToolStripMenuItem("策略组(点选即切换)");
            foreach (var g in groups)
            {
                var gItem = new ToolStripMenuItem($"{g.Name}  ⇒  {g.Now}");
                foreach (var opt in g.Options)
                {
                    var oItem = new ToolStripMenuItem(opt)
                    {
                        Checked = string.Equals(opt, g.Now, StringComparison.Ordinal)
                    };
                    var capturedGroup = g.Name;
                    var capturedOpt = opt;
                    oItem.Click += async (_, _) =>
                        await SafeSelectAsync(capturedGroup, capturedOpt);
                    gItem.DropDownItems.Add(oItem);
                }
                groupsRoot.DropDownItems.Add(gItem);
            }
            menu.Items.Add(groupsRoot);
        }

        menu.Items.Add(new ToolStripSeparator());

        var open = new ToolStripMenuItem($"应用 / 网站分流  ({_settings.Hotkey})");
        open.Click += (_, _) => ShowRouting();
        menu.Items.Add(open);

        var routing = new ToolStripMenuItem("高级面板（模式 / 场景 / 策略组）…");
        routing.Click += (_, _) => ShowPanel();
        menu.Items.Add(routing);

        var refresh = new ToolStripMenuItem("立即刷新");
        refresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refresh);

        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);

        return menu;
    }

    private async Task SafeSetModeAsync(string mode)
    {
        try
        {
            await _client.SetModeAsync(mode);
        }
        catch
        {
            // 忽略
        }
        await RefreshAsync();
    }

    private async Task SafeSelectAsync(string group, string option)
    {
        try
        {
            await _client.SelectAsync(group, option);
        }
        catch
        {
            // 忽略
        }
        await RefreshAsync();
    }

    private async Task SafeApplyPresetAsync(Preset preset)
    {
        try
        {
            await PresetEngine.ApplyAsync(_client, preset);
        }
        catch
        {
            // 忽略单次失败
        }
        await RefreshAsync();
    }

    private async Task SaveCurrentAsPresetAsync()
    {
        using var dlg = new NameDialog();
        if (dlg.ShowDialog() != DialogResult.OK || dlg.Result.Length == 0)
            return;
        try
        {
            var p = await PresetEngine.CaptureAsync(_client, dlg.Result);
            _settings.Presets.Add(p);
            _settings.Save();
        }
        catch
        {
            // 忽略
        }
        await RefreshAsync();
    }

    private void ShowPanel()
    {
        if (_panel is null || _panel.IsDisposed)
        {
            _panel = new PanelForm(_client, _settings);
            _panel.FormClosed += (_, _) => _panel = null;
        }
        _panel.Show();
        _panel.BringToFront();
        _panel.Activate();
    }

    private void ShowRouting()
    {
        if (_routingPanel is null || _routingPanel.IsDisposed)
        {
            _routingPanel = new RoutingForm(_client, _settings);
            _routingPanel.FormClosed += (_, _) => _routingPanel = null;
        }
        _routingPanel.Show();
        if (_routingPanel.WindowState == FormWindowState.Minimized)
            _routingPanel.WindowState = FormWindowState.Normal;
        _routingPanel.BringToFront();
        _routingPanel.Activate();
    }

    private void ExitApp()
    {
        _routingPanel?.Close();
        if (_routingPanel is not null && !_routingPanel.IsDisposed) return;
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _hotkey.Dispose();
        _panel?.Close();
        ExitThread();
    }
}
