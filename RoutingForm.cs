using System.Diagnostics;

namespace ClashDock;

/// <summary>应用 / 网站分流编辑器；运行中的程序选择方式参考 proxifyre-ui。</summary>
public sealed class RoutingForm : Form
{
    private readonly MihomoClient _client;
    private readonly RoutingService _service;
    private readonly Settings _settings;
    private readonly DataGridView _apps = Grid();
    private readonly DataGridView _sites = Grid();
    private readonly ComboBox _proxy = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly TextBox _directory = new() { Width = 420 };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(850, 0), Padding = new Padding(0, 6, 0, 0) };
    private readonly Button _apply = new() { Text = "保存并应用", AutoSize = true };
    private bool _dirty;
    private bool _loading = true;
    private bool _applying;

    public RoutingForm(MihomoClient client, Settings settings)
    {
        _client = client;
        _settings = settings;
        _service = new RoutingService(client, settings);
        Text = "ClashDock · 应用与网站分流";
        ClientSize = new Size(920, 720);
        MinimumSize = new Size(720, 560);
        StartPosition = FormStartPosition.CenterParent;
        Font = Theme.Body;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var intro = new Label
        {
            AutoSize = true, MaximumSize = new Size(850, 0), Padding = new Padding(0, 0, 0, 12),
            Text = "为应用和网站选择：自动 / 直连 / 代理，再点击「保存并应用」。\n" +
                "仅控制已进入 Clash 的流量；网站设置优先，同域名共用设置。"
        };
        root.Controls.Add(intro);
        var settingsRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        settingsRow.Controls.Add(new Label { Text = "代理目标", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        settingsRow.Controls.Add(_proxy);
        var refresh = new Button { Text = "刷新目标", AutoSize = true };
        refresh.Click += async (_, _) => await LoadTargetsAsync();
        settingsRow.Controls.Add(refresh);
        root.Controls.Add(settingsRow);

        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 12) };
        tabs.TabPages.Add(Page("应用", _apps, true));
        tabs.TabPages.Add(Page("网站", _sites, false));
        var options = new TabPage("设置与说明") { BackColor = Theme.Bg, ForeColor = Theme.Text, AutoScroll = true };
        var optionsFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
        optionsFlow.Controls.Add(new Label { Text = "Verge 配置目录（通常自动识别）", AutoSize = true });
        _directory.Text = settings.Routing.VergeDirectory;
        optionsFlow.Controls.Add(_directory);
        var browse = new Button { Text = "选择目录", AutoSize = true };
        browse.Click += (_, _) => { using var dlg = new FolderBrowserDialog { InitialDirectory = _directory.Text }; if (dlg.ShowDialog(this) == DialogResult.OK) _directory.Text = dlg.SelectedPath; };
        optionsFlow.Controls.Add(browse);
        optionsFlow.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(620, 0), Padding = new Padding(0, 14, 0, 0),
            Text = "自动：继续匹配订阅规则。网站设为自动时，也会继续匹配应用设置。\n\n" +
                "应用须遵循系统代理，或已在应用内配置 Clash 代理；这里不会强制接管不支持代理的软件，也不管理虚拟网卡。\n\n" +
                "网站按域名及子域名匹配，适用于所有已进入 Clash 的请求，不限于浏览器；同域名的标签页共用设置。网页引用的其他域名需另行设置。\n\n" +
                "代理使用所选节点或策略组；策略组的实际出口取决于其当前选择。应用设置会切换到规则模式，现有长连接可能需要重新打开。\n\n" +
                "应用时自动备份并更新 Verge 全局扩展脚本。设为自动或删除条目后再次应用，即可撤销对应规则。"
        });
        options.Controls.Add(optionsFlow);
        tabs.TabPages.Add(options);
        root.Controls.Add(tabs);

        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _apply.Click += async (_, _) => await ApplyAsync();
        footer.Controls.Add(_apply);
        root.Controls.Add(footer);
        root.Controls.Add(_status);
        Controls.Add(root);
        Populate(_apps, settings.Routing.Applications);
        Populate(_sites, settings.Routing.Websites);
        foreach (var grid in new[] { _apps, _sites })
        {
            grid.CellValueChanged += (_, _) => Dirty();
            grid.RowsRemoved += (_, _) => Dirty();
            grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.DataError += (_, e) => { e.ThrowException = false; };
        }
        _directory.TextChanged += (_, _) => Dirty();
        _proxy.SelectedIndexChanged += (_, _) => Dirty();
        Load += async (_, _) => { await LoadTargetsAsync(); _loading = false; };
        FormClosing += (_, e) =>
        {
            if (_applying) { e.Cancel = true; return; }
            _apps.EndEdit(); _sites.EndEdit();
            if (_dirty && MessageBox.Show(this, "还有未应用的修改，关闭会丢弃这些修改。仍要关闭？", "未应用的修改", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                e.Cancel = true;
        };
    }

    private TabPage Page(string title, DataGridView grid, bool app)
    {
        var page = new TabPage(title) { BackColor = Theme.Bg, ForeColor = Theme.Text, Padding = new Padding(8) };
        grid.Columns[0].HeaderText = app ? "应用程序" : "网站域名（含子域名）";
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
        var add = new Button { Text = app ? "选择 exe 文件" : "添加网站", AutoSize = true };
        add.Click += (_, _) =>
        {
            if (app)
            {
                using var file = new OpenFileDialog { Filter = "应用程序 (*.exe)|*.exe", CheckFileExists = true };
                if (file.ShowDialog(this) == DialogResult.OK) Add(grid, file.FileName);
            }
            else
            {
                using var dialog = new TextEntryDialog("添加网站", "输入域名或粘贴网址，例如 https://www.bilibili.com");
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    try { Add(grid, RoutingCompiler.Domain(dialog.Value)); } catch (Exception ex) { Error(ex.Message); }
            }
        };
        bar.Controls.Add(add);
        if (app)
        {
            var running = new Button { Text = "从运行中的应用选择", AutoSize = true };
            running.Click += (_, _) => { using var picker = new ApplicationPicker(); if (picker.ShowDialog(this) == DialogResult.OK) Add(grid, picker.SelectedPath); };
            bar.Controls.Add(running);
        }
        var remove = new Button { Text = "删除选中项", AutoSize = true };
        remove.Click += (_, _) => { foreach (var row in grid.SelectedRows.Cast<DataGridViewRow>().ToArray()) if (!row.IsNewRow) grid.Rows.Remove(row); };
        bar.Controls.Add(remove);
        page.Controls.Add(grid);
        page.Controls.Add(bar);
        return page;
    }

    private static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, BackgroundColor = Theme.Card, BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, EnableHeadersVisualStyles = false,
            DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Card, ForeColor = Theme.Text, SelectionBackColor = Theme.CardHover, SelectionForeColor = Theme.Text, Padding = new Padding(6) },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Card2, ForeColor = Theme.Text },
            EditMode = DataGridViewEditMode.EditOnEnter
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Target", HeaderText = "应用路径 / 网站域名", FillWeight = 80, ReadOnly = true });
        grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Mode", HeaderText = "模式", FillWeight = 20, DataSource = new[] { "自动", "直连", "代理" }, FlatStyle = FlatStyle.Flat });
        return grid;
    }

    private static void Populate(DataGridView grid, IEnumerable<RouteEntry> entries)
    {
        foreach (var entry in entries) grid.Rows.Add(entry.Target, entry.Mode);
    }

    private void Add(DataGridView grid, string target)
    {
        if (grid.Rows.Cast<DataGridViewRow>().Any(r => string.Equals(r.Cells[0].Value?.ToString(), target, StringComparison.OrdinalIgnoreCase)))
        { Error("这个条目已经在列表中。"); return; }
        grid.Rows.Add(target, "自动");
        Dirty();
    }

    private void Dirty()
    {
        if (_loading) return;
        _dirty = true;
        _status.ForeColor = Theme.Warn;
        _status.Text = "有未应用的修改。点击「保存并应用」后生效。已有连接可能需要重新打开应用或刷新网页。";
    }

    private async Task LoadTargetsAsync()
    {
        var selected = _proxy.SelectedItem?.ToString() ?? _settings.Routing.Proxy;
        try
        {
            var targets = await _client.GetProxyNamesAsync();
            var wasLoading = _loading;
            _loading = true;
            try { _proxy.Items.Clear(); _proxy.Items.AddRange(targets.Cast<object>().ToArray()); _proxy.SelectedItem = selected; }
            finally { _loading = wasLoading; }
            if (!_dirty) { _status.Text = "已连接。添加应用或网站后，选择模式并应用。"; _status.ForeColor = Theme.Ok; }
        }
        catch { Error("无法连接 Clash。请检查 Verge 外部控制器，以及 settings.json 的地址、端口和密钥。"); }
    }

    private async Task ApplyAsync()
    {
        if (_applying) return;
        _apps.EndEdit(); _sites.EndEdit();
        _applying = true;
        _apply.Enabled = false;
        var draft = new RoutingSettings
        {
            VergeDirectory = _directory.Text.Trim(), Proxy = _proxy.SelectedItem?.ToString() ?? "",
            Applications = Entries(_apps), Websites = Entries(_sites)
        };
        // 禁用整个编辑区，避免等待期间的修改被误标为已应用。
        foreach (Control control in Controls) if (control != _status) control.Enabled = false;
        _status.Text = "正在备份全局脚本并应用分流设置…";
        try
        {
            await _service.ApplyAsync(draft);
            _dirty = false;
            _status.Text = "已保存并确认规则生效。网站优先于应用；现有长连接需重新连接。";
            _status.ForeColor = Theme.Ok;
        }
        catch (Exception ex) { Error(ex.Message); }
        finally
        {
            _applying = false;
            foreach (Control control in Controls) control.Enabled = true;
            _apply.Enabled = true;
        }
    }

    private static List<RouteEntry> Entries(DataGridView grid) => grid.Rows.Cast<DataGridViewRow>()
        .Select(r => new RouteEntry { Target = r.Cells[0].Value?.ToString() ?? "", Mode = r.Cells[1].Value?.ToString() ?? "自动" }).ToList();

    private void Error(string message) { _status.ForeColor = Theme.Err; _status.Text = message; }
}

internal sealed class TextEntryDialog : Form
{
    private readonly TextBox _input = new() { Dock = DockStyle.Top };
    public string Value => _input.Text.Trim();
    public TextEntryDialog(string title, string hint)
    {
        Text = title; ClientSize = new Size(560, 150); Padding = new Padding(14);
        Font = Theme.Body; StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var label = new Label { Text = hint, Dock = DockStyle.Top, Height = 34 };
        var ok = new Button { Text = "添加", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        Controls.Add(_input); Controls.Add(label); Controls.Add(ok); AcceptButton = ok;
    }
}

internal sealed class ApplicationPicker : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    public string SelectedPath => (_list.SelectedItem as AppItem)?.Path ?? "";
    private sealed record AppItem(string Name, string Path) { public override string ToString() => $"{Name}  —  {Path}"; }
    public ApplicationPicker()
    {
        Text = "选择运行中的应用"; ClientSize = new Size(780, 460); Font = Theme.Body; StartPosition = FormStartPosition.CenterParent;
        var filter = new TextBox { Dock = DockStyle.Top, PlaceholderText = "搜索应用名或路径" };
        var items = new List<AppItem>();
        foreach (var process in Process.GetProcesses())
            using (process)
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        items.Add(new AppItem(process.ProcessName, path));
                }
                catch { /* 系统或其他用户的进程可能无法读取路径。 */ }
        items = items.DistinctBy(i => i.Path, StringComparer.OrdinalIgnoreCase).OrderBy(i => i.Name).ToList();
        void Fill() { _list.Items.Clear(); _list.Items.AddRange(items.Where(i => i.ToString().Contains(filter.Text, StringComparison.OrdinalIgnoreCase)).Cast<object>().ToArray()); }
        filter.TextChanged += (_, _) => Fill();
        var ok = new Button { Text = "添加选中应用", Dock = DockStyle.Bottom, Height = 36 };
        ok.Click += (_, _) => { if (_list.SelectedItem is not null) DialogResult = DialogResult.OK; };
        _list.DoubleClick += (_, _) => { if (_list.SelectedItem is not null) DialogResult = DialogResult.OK; };
        Controls.Add(_list); Controls.Add(filter); Controls.Add(ok); Fill();
    }
}
