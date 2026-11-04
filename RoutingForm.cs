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
    private readonly ModernSelect _proxy = new() { Width = 320 };
    private readonly TextBox _directory = new() { Width = 420 };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(850, 0), Padding = new Padding(0, 6, 0, 0) };
    private readonly PillButton _apply = new() { Text = "保存并应用", AutoSize = true };
    private readonly CancellationTokenSource _catalogLife = new();
    private readonly HashSet<string> _configuredApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image> _appIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _appSearch = new();
    private readonly ModernSelect _defaultBehavior = new();
    private readonly PillButton _tunButton = new() { Text = "TUN · 刷新", Outline = true, AutoSize = false };
    private readonly Label _networkStatus = new() { Dock = DockStyle.Fill, ForeColor = Theme.Warn, Font = Theme.Small, AutoEllipsis = true };
    private NetworkSnapshot? _network;
    private bool _rulesVerified;
    private readonly System.Windows.Forms.Timer _networkTimer = new() { Interval = 8000 };
    private Task? _networkRefreshTask;
    private int _networkGeneration;
    private bool _changingNetwork;
    private bool _preview;
    private bool _catalogDisposed;
    private bool _scanBusy;
    private bool _dirty;
    private bool _loading = true;
    private bool _applying;

    public RoutingForm(MihomoClient client, Settings settings, bool preview = false)
    {
        SuspendLayout();
        _preview = preview;
        _client = client;
        _settings = settings;
        _configuredApps.UnionWith(settings.Routing.Applications.Select(x => x.Target));
        _service = new RoutingService(client, settings);
        Text = "ClashDock · 分流工作台";
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        float dpiScale = DeviceDpi / 96f;
        ClientSize = new Size(Math.Min((int)(1020 * dpiScale), area.Width - 60), Math.Min((int)(760 * dpiScale), area.Height - 100));
        MinimumSize = new Size(920, 720);
        StartPosition = FormStartPosition.CenterParent;
        Font = Theme.Body; BackColor = Theme.Bg; ForeColor = Theme.Text;
        DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
        UiStyle.Window(this);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(28, 20, 28, 20), BackColor = Theme.Bg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, TextRenderer.MeasureText("Ag", Theme.Title).Height + 16));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(_proxy.Height, TextRenderer.MeasureText("Ag", Theme.Body).Height + 14) + 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, TextRenderer.MeasureText("Ag", Theme.Small).Height * 2 + 12));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(80, TextRenderer.MeasureText("Ag", Theme.Small).Height * 2 + 12)));
        var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        header.Controls.Add(new Label { Text = "ClashDock", Font = Theme.Title, AutoSize = true, Location = new(0, 0), ForeColor = Theme.Text });
        int captionLeft = TextRenderer.MeasureText("ClashDock", Theme.Title).Width + 20;
        var caption = new Label { Text = "应用与网站分流", Font = Theme.Small, AutoSize = false, AutoEllipsis = true, Location = new(captionLeft, 12), Height = TextRenderer.MeasureText("Ag", Theme.Small).Height + 8, ForeColor = Theme.SubText };
        header.Controls.Add(caption);
        header.Resize += (_, _) => caption.Width = Math.Max(0, header.Width - captionLeft - 220);
        _tunButton.Size = new Size(170, Math.Max(40, TextRenderer.MeasureText("Ag", Theme.Body).Height + 14));
        _tunButton.Click += async (_, _) => await ChangeTunAsync();
        header.Controls.Add(_tunButton); header.Resize += (_, _) => _tunButton.Location = new(Math.Max(0, header.Width - _tunButton.Width), 0);
        root.Controls.Add(header, 0, 0);

        var target = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(16), Margin = new Padding(0, 0, 0, 18) };
        var targetLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Card };
        targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TextRenderer.MeasureText("代理目标", Theme.Body).Width + 18));
        targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TextRenderer.MeasureText("刷新目标", Theme.Body).Width + 36));
        targetLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        targetLayout.Controls.Add(new Label { Text = "代理目标", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.SubText });
        UiStyle.Input(_proxy); _proxy.Dock = DockStyle.Fill; _proxy.Margin = new Padding(0, 1, 16, 0);
        targetLayout.Controls.Add(_proxy);
        var refresh = new PillButton { Text = "刷新目标", Outline = true, SurfaceColor = Theme.Card, AutoSize = false, Dock = DockStyle.Fill, Margin = Padding.Empty };
        refresh.Click += async (_, _) => { refresh.Enabled = false; try { await LoadTargetsAsync(); } finally { if (!refresh.IsDisposed) refresh.Enabled = true; } };
        targetLayout.Controls.Add(refresh); target.Controls.Add(targetLayout); root.Controls.Add(target, 0, 1);

        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Math.Max(140, TextRenderer.MeasureText("设置与说明", Theme.Body).Width + 54))); workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 18, 0) };
        var pages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Margin = Padding.Empty };
        var appPage = Page("应用", _apps, true); var sitePage = Page("网站", _sites, false);
        var options = OptionsPage(settings);
        var sections = new[] { ("应用规则", (Control)appPage), ("网站规则", (Control)sitePage), ("设置与说明", (Control)options) };
        var navButtons = new List<PillButton>();
        foreach (var (name, page) in sections)
        {
            page.Dock = DockStyle.Fill; pages.Controls.Add(page);
            var button = new PillButton { Text = name, AutoSize = false, Size = new(Math.Max(120, TextRenderer.MeasureText("设置与说明", Theme.Body).Width + 34), Math.Max(42, TextRenderer.MeasureText("Ag", Theme.Body).Height + 18)), Margin = new Padding(0, 0, 0, 8), SurfaceColor = Theme.Bg };
            navButtons.Add(button); nav.Controls.Add(button);
            button.Click += (_, _) => { foreach (var item in sections) item.Item2.Visible = item.Item2 == page; foreach (var item in navButtons) { item.Accent = item == button; item.Invalidate(); } page.BringToFront(); };
        }
        nav.Controls.Add(new Label { Text = "规则优先级\n\n网站规则\n↓\n应用规则\n↓\n订阅规则", AutoSize = false, Size = new(Math.Max(120, TextRenderer.MeasureText("规则优先级", Theme.Small).Width + 28), 260), Font = Theme.Small, ForeColor = Theme.SubText, Padding = new Padding(12, 24, 0, 0) });
        workspace.Controls.Add(nav, 0, 0); workspace.Controls.Add(pages, 1, 0); root.Controls.Add(_networkStatus, 0, 2); root.Controls.Add(workspace, 0, 3);
        navButtons[0].PerformClick();
        // PerformClick is ignored before a form is visible; establish initial state explicitly.
        appPage.Visible = true; sitePage.Visible = options.Visible = false; appPage.BringToFront(); navButtons[0].Accent = true;

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 14, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        _status.AutoSize = false; _status.MaximumSize = Size.Empty; _status.Dock = DockStyle.Fill; _status.Padding = new Padding(0, 4, 16, 0); _status.Font = Theme.Small; _status.ForeColor = Theme.SubText;
        _status.Text = "连接就绪后，添加应用或网站，选择路由方式并保存。"; _status.AutoEllipsis = true;
        footer.Controls.Add(_status, 0, 0);
        _apply.Accent = true; _apply.SurfaceColor = Theme.Bg; _apply.AutoSize = false; _apply.Size = new Size(148, Math.Max(42, TextRenderer.MeasureText("Ag", Theme.Body).Height + 18)); _apply.Anchor = AnchorStyles.Right | AnchorStyles.Top; _apply.Margin = Padding.Empty;
        _apply.Click += async (_, _) => await ApplyAsync(); footer.Controls.Add(_apply, 1, 0); root.Controls.Add(footer, 0, 4);
        _status.Cursor = Cursors.Hand; _status.AccessibleName = "操作结果，点击查看完整详情";
        _status.Click += (_, _) => NetworkConfirmation.ShowDetails(this, _status.Text);
        _networkStatus.Cursor = Cursors.Hand; _networkStatus.Click += (_, _) => NetworkConfirmation.ShowDetails(this, _networkStatus.Text);
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
        Load += async (_, _) => { if (!preview) { _ = DiscoverAppsAsync(); await LoadTargetsAsync(); } else { _proxy.Items.Add("示例代理策略组"); _proxy.SelectedIndex = 0; _status.Text = "界面预览 · 隔离示例，不连接控制器或修改代理。"; } if (preview) UpdateNetworkLabel(); _loading = false; };
        _networkTimer.Tick += async (_, _) => { if (!_applying && !_changingNetwork && Visible) await RefreshNetworkAsync(); };
        VisibleChanged += (_, _) => { if (!_preview && !_catalogDisposed) { if (Visible) _networkTimer.Start(); else _networkTimer.Stop(); } };
        ResumeLayout(true);
        FormClosing += (_, e) =>
        {
            if (_applying || _changingNetwork) { e.Cancel = true; return; }
            _apps.EndEdit(); _sites.EndEdit();
            if (_dirty && MessageBox.Show(this, "还有未应用的修改，关闭会丢弃这些修改。仍要关闭？", "未应用的修改", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                e.Cancel = true;
        };
    }

    private Panel Page(string title, DataGridView grid, bool app)
    {
        var page = new Panel { BackColor = Theme.Card, ForeColor = Theme.Text, Padding = new Padding(18) };
        if (!app) ((DataGridViewComboBoxColumn)grid.Columns[1]).DataSource = new[] { "自动", "直连", "代理" };
        grid.Columns[0].HeaderText = app ? "应用程序" : "网站域名（含子域名）";
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 14), WrapContents = true, BackColor = Theme.Card };
        var heading = new Panel { Dock = DockStyle.Top, Height = TextRenderer.MeasureText("Ag", Theme.Sub).Height + 18, BackColor = Theme.Card };
        heading.Controls.Add(new Label { Text = app ? "应用规则" : "网站规则", Font = Theme.Sub, AutoSize = true, Location = new(0, 0) });

        var add = new PillButton { Text = app ? "扫描应用" : "添加网站", Accent = true, SurfaceColor = Theme.Card };
        add.Click += async (_, _) =>
        {
            if (app)
            {
                add.Enabled = false; try { await DiscoverAppsAsync(); } finally { if (!add.IsDisposed) add.Enabled = true; }
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
            var running = new PillButton { Text = "手动补充", Outline = true, SurfaceColor = Theme.Card };
            running.Click += (_, _) => { using var file = new OpenFileDialog { Filter = "应用程序 (*.exe)|*.exe", CheckFileExists = true }; if (file.ShowDialog(this) == DialogResult.OK) Add(grid, file.FileName); };
            bar.Controls.Add(running);
        }
        var remove = new PillButton { Text = app ? "恢复默认" : "移除", Outline = true, SurfaceColor = Theme.Card };
        remove.Click += (_, _) => { foreach (var row in grid.SelectedRows.Cast<DataGridViewRow>().ToArray()) if (!row.IsNewRow) { if (app) row.Cells[1].Value = "默认"; else grid.Rows.Remove(row); } };
        bar.Controls.Add(remove);
        page.Controls.Add(grid);
        page.Controls.Add(bar); page.Controls.Add(heading);
        if (app)
        {
            var searchRow = new Panel { Dock = DockStyle.Top, Height = TextRenderer.MeasureText("Ag", Theme.Body).Height + 28 };
            _appSearch.PlaceholderText = "搜索应用名称或路径"; UiStyle.Input(_appSearch); _appSearch.Dock = DockStyle.Top;
            searchRow.Controls.Add(_appSearch); page.Controls.Add(searchRow); page.Controls.SetChildIndex(searchRow, 1);
            _appSearch.TextChanged += (_, _) => FilterApps();
        }
        else
        {
            var common = new PillButton { Text = "常用站点", Outline = true, SurfaceColor = Theme.Card };
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Body };
            foreach (var (name, domain) in new[] { ("GitHub", "github.com"), ("Google", "google.com"), ("YouTube", "youtube.com"), ("ChatGPT", "chatgpt.com"), ("Gemini", "gemini.google.com"), ("哔哩哔哩", "bilibili.com") })
                menu.Items.Add(name, null, (_, _) => Add(grid, domain));
            common.Click += (_, _) => menu.Show(common, new Point(0, common.Height));
            common.Disposed += (_, _) => menu.Dispose(); bar.Controls.Add(common);
        }
        grid.CellPainting += (_, e) => PaintRule(grid, e, app);
        grid.Paint += (_, e) => { if (grid.Rows.Count == 0) TextRenderer.DrawText(e.Graphics, app ? "正在发现应用或没有匹配结果\n可点击「扫描应用」重新扫描" : "还没有网站规则\n点击「添加网站」开始设置", Theme.Body, new Rectangle(12, 100, Math.Max(1, grid.Width - 24), 70), Theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak); };
        return page;
    }

    private Panel OptionsPage(Settings settings)
    {
        var page = new Panel { BackColor = Theme.Card, Padding = new Padding(24), AutoScroll = true };
        var content = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        content.Controls.Add(new Label { Text = "设置与说明", Font = Theme.Sub, AutoSize = true, Margin = new Padding(0, 0, 0, 16) });
        content.Controls.Add(new Label { Text = "未单独设置的应用", Font = Theme.Sub, AutoSize = true, Margin = new Padding(0, 0, 0, 10) });
        _defaultBehavior.SetItems(new[] { "沿用订阅规则", "未单独设置的应用全部直连" }, settings.Routing.DefaultBehavior);
        _defaultBehavior.Width = 420; _defaultBehavior.Margin = new Padding(0, 0, 0, 14);
        _defaultBehavior.SelectedIndexChanged += (_, _) => { Dirty(); _apps.Invalidate(); UpdateNetworkLabel(); };
        content.Controls.Add(_defaultBehavior);
        content.Controls.Add(new Label { Text = "自动＝明确沿用订阅；默认＝未单设。修改需保存并确认。", AutoSize = true, ForeColor = Theme.SubText, Font = Theme.Small, Margin = new Padding(0, 0, 0, 24) });
        content.Controls.Add(new Label { Text = "Verge 配置目录", Font = Theme.Sub, AutoSize = true, Margin = new Padding(0, 0, 0, 8) });
        _directory.Text = settings.Routing.VergeDirectory; UiStyle.Input(_directory); _directory.PlaceholderText = "留空则自动识别配置目录";
        content.Controls.Add(_directory);
        var browse = new PillButton { Text = "选择文件夹", Outline = true, SurfaceColor = Theme.Card, Margin = new Padding(0, 12, 0, 22) };
        browse.Click += (_, _) => { using var dlg = new FolderBrowserDialog { InitialDirectory = _directory.Text }; if (dlg.ShowDialog(this) == DialogResult.OK) _directory.Text = dlg.SelectedPath; };
        content.Controls.Add(browse);
        var help = new Label { AutoSize = true, MaximumSize = new Size(600, 0), Font = Theme.Small, ForeColor = Theme.SubText, Text = "三种路由方式\n\n默认   采用未单设应用的默认行为。\n自动   明确沿用订阅规则（不受默认直连影响）。\n直连   不经过代理节点。\n代理   使用上方选择的节点或策略组。\n\n使用范围\n\n仅处理已进入 Clash 的流量。应用需遵循系统代理，或已配置 Clash 代理；这里不会强制接管软件或管理虚拟网卡。\n\n网站按域名及子域名匹配，网站规则优先于应用规则。网页引用的其他域名需另行设置。\n\n保存时会备份并更新 Verge 全局扩展脚本，切换到规则模式。现有长连接可能需要重连。将条目设为自动或移除后再次保存，可撤销对应规则。" };
        content.Controls.Add(help);
        var advanced = new PillButton { Text = "高级连接配置…", Outline = true, SurfaceColor = Theme.Card, Margin = new Padding(0, 20, 0, 12) };
        advanced.Click += (_, _) => { if (!_preview) { AdvancedConnection.Show(this, _settings); _status.Text = "连接配置如有更改，重启 ClashDock 后使用；未更改网络规则。"; } };
        content.Controls.Add(advanced); page.Controls.Add(content);
        page.Resize += (_, _) => { int width = Math.Max(150, page.ClientSize.Width - page.Padding.Horizontal - 20); _directory.Width = width; _defaultBehavior.Width = width; help.MaximumSize = new Size(width, 0); };
        return page;
    }

    private void PaintRule(DataGridView grid, DataGridViewCellPaintingEventArgs e, bool app)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics == null) return;
        var g = e.Graphics; e.PaintBackground(e.ClipBounds, true);
        var r = e.CellBounds; var value = e.Value?.ToString() ?? "";
        if (e.ColumnIndex == 0)
        {
            var discovered = grid.Rows[e.RowIndex].Tag as DiscoveredApp;
            var name = app ? discovered?.Name ?? Path.GetFileNameWithoutExtension(value) : value;
            int inset = app ? 52 : 14;
            if (app)
            {
                if (!_appIcons.TryGetValue(value, out var image) && _appIcons.Count < 128)
                {
                    try { using var icon = Icon.ExtractAssociatedIcon(value); if (icon != null) _appIcons[value] = image = icon.ToBitmap(); } catch (Exception error) when (error is ArgumentException or IOException or System.ComponentModel.Win32Exception) { }
                }
                if (image != null) g.DrawImage(image, new Rectangle(r.X + 14, r.Y + (r.Height - 26) / 2, 26, 26));
            }
            grid.Rows[e.RowIndex].Cells[0].ToolTipText = value;
            TextRenderer.DrawText(g, name, Theme.Sub, new Rectangle(r.X + inset, r.Y + 10, Math.Max(1, r.Width - inset - 12), TextRenderer.MeasureText("Ag", Theme.Sub).Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, app ? ApplicationState(value, grid.Rows[e.RowIndex].Cells[1].Value?.ToString() ?? "默认") : "域名及其子域名", Theme.Small, new Rectangle(r.X + inset, r.Y + 16 + TextRenderer.MeasureText("Ag", Theme.Sub).Height, Math.Max(1, r.Width - inset - 12), TextRenderer.MeasureText("Ag", Theme.Small).Height), Theme.SubText, TextFormatFlags.EndEllipsis);
        }
        else
        {
            var color = value == "直连" ? Theme.Ok : value == "代理" ? Theme.AccentHover : Theme.SubText;
            int pillHeight = TextRenderer.MeasureText("Ag", Theme.Body).Height + 10;
            var pill = new Rectangle(r.X + 10, r.Y + (r.Height - pillHeight) / 2, Math.Max(1, r.Width - 20), pillHeight);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = Gfx.Rounded(pill, 8); using var fill = new SolidBrush(Color.FromArgb(30, color)); using var border = new Pen(Color.FromArgb(75, color));
            g.FillPath(fill, path); g.DrawPath(border, path);
            TextRenderer.DrawText(g, value, Theme.Body, new Rectangle(pill.X + 12, pill.Y, pill.Width - 34, pill.Height), color, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using var arrow = new Pen(color, 1.4f); int x = pill.Right - 15, y = pill.Top + pill.Height / 2; g.DrawLines(arrow, new[] { new Point(x - 3, y - 1), new Point(x, y + 2), new Point(x + 3, y - 1) });
        }
        using var line = new Pen(Theme.Border); g.DrawLine(line, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
        e.Handled = true;
    }

    private static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, BackgroundColor = Theme.Card, BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = TextRenderer.MeasureText("Ag", Theme.Body).Height + 20, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, GridColor = Theme.Border, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, AllowUserToResizeRows = false,
            DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Card, ForeColor = Theme.Text, SelectionBackColor = Theme.CardHover, SelectionForeColor = Theme.Text, Padding = new Padding(6) },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Card2, ForeColor = Theme.Text },
            EditMode = DataGridViewEditMode.EditOnEnter
        };
        grid.RowTemplate.Height = TextRenderer.MeasureText("Ag", Theme.Sub).Height + TextRenderer.MeasureText("Ag", Theme.Small).Height + 30;
        grid.EditingControlShowing += (_, e) => { if (e.Control is ComboBox combo) { combo.BackColor = Theme.Card2; combo.ForeColor = Theme.Text; combo.FlatStyle = FlatStyle.Flat; } };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Target", HeaderText = "应用路径 / 网站域名", FillWeight = 80, ReadOnly = true });
        grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Mode", HeaderText = "模式", FillWeight = 20, MinimumWidth = 116, DataSource = new[] { "默认", "代理", "直连", "自动" }, FlatStyle = FlatStyle.Flat });
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

    private async Task DiscoverAppsAsync()
    {
        if (_scanBusy || IsDisposed) return;
        _scanBusy = true;
        try
        {
            var found = await ApplicationCatalog.ScanAsync(_catalogLife.Token);
            if (IsDisposed || _catalogLife.IsCancellationRequested) return;
            MergeApps(found);
            if (!_dirty && _status.ForeColor != Theme.Err) { _status.Text = $"发现 {found.Length} 个应用。默认沿用上方默认行为；选择代理／直连／自动后保存。"; _status.ForeColor = Theme.SubText; }
        }
        catch (OperationCanceledException) { }
        catch { if (!IsDisposed) Error("部分应用未能识别。可重新扫描，或使用手动补充；现有规则保留。"); }
        finally { _scanBusy = false; }
    }

    internal void MergeApps(IEnumerable<DiscoveredApp> apps)
    {
        bool loading = _loading; _loading = true;
        _apps.SuspendLayout();
        try
        {
            var existing = _apps.Rows.Cast<DataGridViewRow>().ToDictionary(r => r.Cells[0].Value?.ToString() ?? "", StringComparer.OrdinalIgnoreCase);
            foreach (var app in ApplicationCatalog.Normalize(apps))
            {
                if (!existing.TryGetValue(app.Path, out var row)) { int index = _apps.Rows.Add(app.Path, "默认"); row = _apps.Rows[index]; existing[app.Path] = row; }
                row.Tag = app;
            }
            FilterApps();
        }
        finally { _apps.ResumeLayout(); _loading = loading; }
    }

    private void FilterApps()
    {
        string text = _appSearch.Text.Trim(); _apps.CurrentCell = null;
        foreach (DataGridViewRow row in _apps.Rows)
        {
            var app = row.Tag as DiscoveredApp;
            row.Visible = text.Length == 0 || (app?.Name ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) || (row.Cells[0].Value?.ToString() ?? "").Contains(text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private string ApplicationState(string path, string mode)
    {
        var saved = _settings.Routing.Applications.FirstOrDefault(x => x.Target.Equals(path, StringComparison.OrdinalIgnoreCase))?.Mode ?? "默认";
        if (mode != saved || (mode == "默认" && _defaultBehavior.SelectedItem != _settings.Routing.DefaultBehavior) || (mode == "代理" && _proxy.SelectedItem != _settings.Routing.Proxy)) return "草稿 · 保存并确认后生效";
        string label = mode == "默认" ? (_settings.Routing.DefaultBehavior == "沿用订阅规则" ? "默认沿用订阅" : "默认直连") : mode == "自动" ? "明确沿用订阅" : mode;
        return label + (!_rulesVerified || _network?.Mode != "rule" ? " · 内核规则待核验" : _network.Enabled && NetworkSnapshot.AdapterUp(_network) ? " · 规则已加载，需重连验证" : " · TUN未确认，部分应用可能无效");
    }
    private void UpdateNetworkLabel()
    {
        if (IsDisposed) return;
        _networkStatus.Text = (_network?.Describe(_network != null && NetworkSnapshot.AdapterUp(_network)) ?? "TUN／运行模式尚未读取；不自动更改网络") + "\n默认行为（草稿）：" + (_defaultBehavior.SelectedItem ?? "沿用订阅规则");
        _networkStatus.ForeColor = _network?.Enabled == true && NetworkSnapshot.AdapterUp(_network) && _network.Mode == "rule" ? Theme.Ok : Theme.Warn;
        _tunButton.Text = _network?.Enabled == true ? "关闭 TUN" : "开启 TUN";
        _apps.Invalidate();
    }
    private Task RefreshNetworkAsync(bool force = false)
    {
        if (_preview) { UpdateNetworkLabel(); return Task.CompletedTask; }
        if (!force && _networkRefreshTask is { IsCompleted: false }) return _networkRefreshTask;
        return _networkRefreshTask = ReadNetworkAsync(++_networkGeneration);
    }
    private async Task ReadNetworkAsync(int generation)
    {
        try
        {
            var network = await _client.GetNetworkAsync(); var rules = await _client.GetRuleSequenceAsync();
            if (IsDisposed || generation != _networkGeneration) return; _network = network;
            _rulesVerified = RoutingCompiler.RulesConfirmed(_settings.Routing, rules);
        }
        catch { if (IsDisposed || generation != _networkGeneration) return; _network = null; _rulesVerified = false; }
        UpdateNetworkLabel();
    }
    private async Task ChangeTunAsync()
    {
        if (_preview || _changingNetwork || _applying) return;
        _networkGeneration++;
        _changingNetwork = true; _tunButton.Enabled = _apply.Enabled = false;
        try
        {
            var current = await _client.GetNetworkAsync(); bool enabled = !current.Enabled;
            string detail = enabled
                ? $"当前模式 {current.Mode}；将开启 TUN、自动路由并切换为规则模式。\n网卡：{(current.Device.Length > 0 ? current.Device : "ClashDock")}。保留已有 DNS、协议栈及其他 TUN 设置。\n可能影响系统路由和现有连接；需要 Verge 服务模式／足够权限。不会保存应用草稿，失败尝试恢复原状态。"
                : "将关闭当前 TUN 接管，规则和未保存草稿不变；不走系统代理的应用可能改为绕过 Clash。";
            if (!NetworkConfirmation.Ask(this, detail)) return;
            _status.Text = "正在请求内核并核验 TUN 状态…";
            _status.Text = await new TunControl(_client).SetAsync(enabled); _status.ForeColor = Theme.Ok;
        }
        catch (Exception error) { Error(error.Message); }
        finally { if (!IsDisposed) { await RefreshNetworkAsync(true); _changingNetwork = false; _tunButton.Enabled = _apply.Enabled = true; } }
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
            try { _proxy.SetItems(targets, targets.Contains(selected) ? selected : targets.FirstOrDefault()); }
            finally { _loading = wasLoading; }
            await RefreshNetworkAsync();
            if (!_dirty) { _status.Text = "已连接。应用会自动列出；选择路由方式后保存即可。"; _status.ForeColor = Theme.Ok; }
        }
        catch { _network = null; _rulesVerified = false; UpdateNetworkLabel(); Error("未连接到 Clash。请先启动 Verge 并启用外部控制器；工具会自动读取本机连接配置。便携版可在设置中选择配置文件夹。"); }
    }

    private async Task ApplyAsync()
    {
        if (_applying || _changingNetwork || _preview) return;
        _apps.EndEdit(); _sites.EndEdit();
        var draft = new RoutingSettings
        {
            VergeDirectory = _directory.Text.Trim(), Proxy = _proxy.SelectedItem?.ToString() ?? "",
            Applications = Entries(_apps), Websites = Entries(_sites), DefaultBehavior = _defaultBehavior.SelectedItem ?? "沿用订阅规则"
        };
        try
        {
            RoutingCompiler.Rules(draft);
            var current = await _client.GetNetworkAsync();
            if (!NetworkConfirmation.Ask(this, NetworkConfirmation.Preview(_settings.Routing, draft, current))) return;
        }
        catch (Exception error) { Error(error.Message); return; }
        _networkGeneration++;
        _applying = true; _apply.Enabled = false;
        // 禁用整个编辑区，避免等待期间的修改被误标为已应用。
        foreach (Control control in Controls) if (control != _status) control.Enabled = false;
        _status.Text = "正在备份全局脚本并应用分流设置…";
        try
        {
            await _service.ApplyAsync(draft);
            _dirty = false;
            _status.Text = "规则模式与托管规则已被内核确认；现有连接需重连，尚未实测每个应用出口。";
            _status.ForeColor = Theme.Ok;
        }
        catch (Exception ex) { Error(ex.Message); }
        finally
        {
            await RefreshNetworkAsync(true);
            _applying = false;
            foreach (Control control in Controls) control.Enabled = true;
            _apply.Enabled = true;
        }
    }

    private List<RouteEntry> Entries(DataGridView grid) => grid.Rows.Cast<DataGridViewRow>()
        .Where(r => grid != _apps || r.Cells[1].Value?.ToString() != "默认")
        .Select(r => new RouteEntry { Target = r.Cells[0].Value?.ToString() ?? "", Mode = r.Cells[1].Value?.ToString() ?? "自动" }).ToList();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_catalogDisposed)
        {
            _catalogDisposed = true; _networkGeneration++; _networkTimer.Stop(); _networkTimer.Dispose(); _catalogLife.Cancel(); _catalogLife.Dispose();
            foreach (var icon in _appIcons.Values) icon.Dispose(); _appIcons.Clear();
        }
        base.Dispose(disposing);
    }

    private void Error(string message) { _status.ForeColor = Theme.Err; _status.Text = message; }
}

internal sealed class TextEntryDialog : Form
{
    private readonly TextBox _input = new() { Dock = DockStyle.Top };
    public string Value => _input.Text.Trim();
    public TextEntryDialog(string title, string hint)
    {
        Text = title; ClientSize = new Size(560, 190); Padding = new Padding(24);
        UiStyle.Window(this); UiStyle.Input(_input); _input.PlaceholderText = "例如 example.com 或 https://example.com";
        Font = Theme.Body; StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var label = new Label { Text = hint, Dock = DockStyle.Top, Height = 34 };
        var ok = new PillButton { Text = "添加网站", Accent = true, DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 40 };
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
        UiStyle.Window(this); _list.BackColor = Theme.Card; _list.ForeColor = Theme.Text; _list.BorderStyle = BorderStyle.None; _list.ItemHeight = 32;
        Text = "选择运行中的应用"; ClientSize = new Size(780, 460); Font = Theme.Body; StartPosition = FormStartPosition.CenterParent;
        var filter = new TextBox { Dock = DockStyle.Top, PlaceholderText = "搜索应用名或路径" };
        UiStyle.Input(filter);
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
        var ok = new PillButton { Text = "添加选中应用", Accent = true, Dock = DockStyle.Bottom, Height = 42 };
        ok.Click += (_, _) => { if (_list.SelectedItem is not null) DialogResult = DialogResult.OK; };
        _list.DoubleClick += (_, _) => { if (_list.SelectedItem is not null) DialogResult = DialogResult.OK; };
        Controls.Add(_list); Controls.Add(filter); Controls.Add(ok); Fill();
    }
}
