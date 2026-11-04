using System.Drawing.Drawing2D;

namespace ClashDock;

/// <summary>深色现代风格可视化面板：模式 / 场景预设 / 策略组。</summary>
public sealed class PanelForm : Form
{
    private readonly MihomoClient _client;
    private readonly Settings _settings;
    private readonly ScrollPanel _flow;
    private readonly PillFlow _presetRow;
    private readonly Label _status;
    private readonly ModernSelect _modeSelect = new();
    private readonly PillButton _refreshBtn = new();
    private readonly Panel _header;
    private readonly CardPanel _modeCard;
    private readonly CardPanel _presetCard;
    private bool _updating;

    public PanelForm(MihomoClient client, Settings settings)
    {
        _client = client;
        _settings = settings;

        Text = "ClashDock · 代理调度";
        ClientSize = new Size(720, 760);
        MinimumSize = new Size(560, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        DoubleBuffered = true;

        // ---- 顶栏（高度按标题字体实测） ----
        int titleH = TextRenderer.MeasureText("ClashDock", Theme.Title).Height;
        int subH = TextRenderer.MeasureText("代理调度面板", Theme.Small).Height;
        _header = new Panel { Dock = DockStyle.Top, Height = Math.Max(88, titleH + subH + 48) };
        _header.Paint += HeaderPaint;
        _header.Resize += (_, _) => LayoutHeader();

        _status = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = Theme.SubText,
            Font = Theme.Small,
            Text = "正在连接…"
        };
        _header.Controls.Add(_status);

        _refreshBtn.Text = "刷新";
        _refreshBtn.Outline = true;
        _refreshBtn.SurfaceColor = Theme.Bg;
        _refreshBtn.Click += async (_, _) => await ReloadAsync();
        _header.Controls.Add(_refreshBtn);

        // ---- 主体滚动区 ----
        _flow = new ScrollPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 8, 22, 22)
        };
        _flow.Resize += (_, _) => AdjustWidths();

        // 运行模式卡片（内容起点按标题实测高度）
        _modeCard = NewCard("运行模式", "rule 规则 · global 全局 · direct 直连");
        _modeSelect.SetItems(new[] { "rule", "global", "direct" }, "rule");
        _modeSelect.Location = new Point(20, _modeCard.ContentTop);
        _modeSelect.Width = 220;
        _modeCard.Height = _modeCard.ContentTop + _modeSelect.Height + 18;
        _modeSelect.SelectedIndexChanged += async (_, _) =>
        {
            if (_updating || _modeSelect.SelectedItem is not string m)
                return;
            await SafeAsync(() => _client.SetModeAsync(m));
            await ReloadAsync();
        };
        _modeCard.Controls.Add(_modeSelect);
        _flow.Controls.Add(_modeCard);

        // 场景预设卡片（按钮自动换行，高度按内容）
        _presetCard = NewCard("场景预设", "一键套用：切换模式并批量应用多个策略组");
        _presetRow = new PillFlow
        {
            Location = new Point(20, _presetCard.ContentTop),
            Width = 200,
            Height = 36,
            Font = Theme.Body
        };
        _presetCard.Controls.Add(_presetRow);
        _flow.Controls.Add(_presetCard);

        // 策略组标题
        var groupHeader = new Label
        {
            Text = "策略组",
            Font = Theme.Sub,
            ForeColor = Theme.Text,
            AutoSize = true
        };
        _flow.Controls.Add(groupHeader);

        // 底部提示
        var hint = new Label
        {
            Text = "提示：下拉即切换，立即生效。DIRECT 为直连，节点名表示走该节点。",
            Font = Theme.Small,
            ForeColor = Theme.SubText,
            AutoSize = true
        };
        _flow.Controls.Add(hint);

        Controls.Add(_flow);
        Controls.Add(_header);

        BuildPresetButtons();

        Load += async (_, _) => await ReloadAsync();
    }

    // ---------------------------------------------------------------- 顶栏绘制

    private void HeaderPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var b = new SolidBrush(Theme.Card))
            g.FillRectangle(b, 0, 0, _header.Width, _header.Height);

        using (var sep = new Pen(Theme.Border, 1f))
            g.DrawLine(sep, 0, _header.Height - 1, _header.Width, _header.Height - 1);

        // 标题块按字体实测垂直居中
        int titleH = TextRenderer.MeasureText("ClashDock", Theme.Title).Height;
        int subH = TextRenderer.MeasureText("代理调度面板", Theme.Small).Height;
        int blockH = titleH + 6 + subH;
        int top = Math.Max(12, (_header.Height - blockH) / 2 - 2);

        using (var dot = new SolidBrush(Theme.Accent))
        using (var p = Gfx.Rounded(new Rectangle(22, top + 2, 8, titleH - 2), 4))
            g.FillPath(dot, p);

        TextRenderer.DrawText(g, "ClashDock", Theme.Title, new Point(42, top), Theme.Text);
        TextRenderer.DrawText(g, "代理调度面板", Theme.Small,
            new Point(43, top + titleH + 6), Theme.SubText);
    }

    private void LayoutHeader()
    {
        _refreshBtn.Location = new Point(
            _header.Width - _refreshBtn.Width - 22,
            (_header.Height - _refreshBtn.Height) / 2);
        _status.Height = TextRenderer.MeasureText("Ag", _status.Font).Height + 4;
        _status.Location = new Point(160, (_header.Height - _status.Height) / 2);
        _status.Width = Math.Max(10, _refreshBtn.Left - 180);
    }

    // ---------------------------------------------------------------- 卡片与预设

    private CardPanel NewCard(string title, string subtitle) => new()
    {
        Title = title,
        Subtitle = subtitle,
        Height = 120,
        Margin = new Padding(0, 0, 0, 14)
    };

    private void BuildPresetButtons()
    {
        _presetRow.Controls.Clear();
        foreach (var p in _settings.Presets)
        {
            var captured = p;
            var btn = new PillButton { Text = captured.Name, Accent = true };
            btn.Click += async (_, _) =>
            {
                await SafeAsync(() => PresetEngine.ApplyAsync(_client, captured));
                await ReloadAsync();
            };
            _presetRow.Controls.Add(btn);
        }

        var save = new PillButton { Text = "＋ 保存当前", Outline = true };
        save.SurfaceColor = Theme.Card;
        save.Click += async (_, _) => await SaveCurrentAsPresetAsync();
        _presetRow.Controls.Add(save);

        _presetRow.Relayout();
        _presetCard.Height = _presetRow.Top + _presetRow.Height + 18;
        AdjustWidths();
    }

    private void AdjustWidths()
    {
        int w = _flow.ClientSize.Width - _flow.Padding.Horizontal - 2;
        if (w < 240)
            return;

        // 预设按钮按新宽度自动换行，并回填卡片高度
        _presetRow.Width = Math.Max(120, w - 44);
        _presetRow.Relayout();
        _presetCard.Height = _presetRow.Top + _presetRow.Height + 18;

        // 手动纵向排列（普通 Panel 不做自动布局）
        int y = _flow.Padding.Top;
        foreach (Control c in _flow.Controls)
        {
            c.Left = _flow.Padding.Left;
            c.Top = y;

            if (c is CardPanel or GroupRow)
            {
                c.Width = w;
                y += c.Height + 16;
            }
            else if (c is Label lbl)
            {
                lbl.Left = _flow.Padding.Left + 2;
                y += lbl.Height + 10;
            }
            else
            {
                y += c.Height + 12;
            }
        }
    }

    // ---------------------------------------------------------------- 数据加载

    private async Task ReloadAsync()
    {
        try
        {
            var mode = await _client.GetModeAsync();
            var groups = await _client.GetSelectorGroupsAsync();

            _updating = true;
            _modeSelect.SelectedItem = mode;
            _updating = false;

            _status.Text = $"已连接 · 模式 {mode} · 共 {groups.Count} 个策略组";
            _status.ForeColor = Theme.Ok;

            RebuildGroups(groups);
        }
        catch
        {
            _status.Text = "未连接到 Clash：请确认 Verge 正在运行且外部控制器已开启";
            _status.ForeColor = Theme.Err;
        }
    }

    private void RebuildGroups(List<ProxyGroup> groups)
    {
        // 移除旧行（保留前 3 个固定控件：模式卡片、预设卡片、策略组标题）
        for (int i = _flow.Controls.Count - 1; i >= 0; i--)
        {
            if (_flow.Controls[i] is GroupRow)
                _flow.Controls.RemoveAt(i);
        }

        _flow.SuspendLayout();
        int insertAt = 3; // 固定控件之后
        foreach (var g in groups)
        {
            var row = new GroupRow { GroupName = g.Name, Value = g.Now };
            row.FitToContent();
            row.Selector.SetItems(g.Options, g.Now);
            var capturedGroup = g.Name;
            row.Selector.SelectedIndexChanged += async (_, _) =>
            {
                if (_updating || row.Selector.SelectedItem is not string opt)
                    return;
                await SafeAsync(() => _client.SelectAsync(capturedGroup, opt));
            };
            _flow.Controls.Add(row);
            _flow.Controls.SetChildIndex(row, insertAt++);
        }
        _flow.ResumeLayout();

        AdjustWidths();
    }

    private async Task SaveCurrentAsPresetAsync()
    {
        using var dlg = new NameDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result.Length == 0)
            return;

        await SafeAsync(async () =>
        {
            var p = await PresetEngine.CaptureAsync(_client, dlg.Result);
            _settings.Presets.Add(p);
            _settings.Save();
        });
        BuildPresetButtons();
        _status.Text = "已保存为场景预设。";
        _status.ForeColor = Theme.Ok;
    }

    private async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            // 忽略单次失败，等待下次刷新
        }
    }
}

/// <summary>深色风格命名对话框。</summary>
internal sealed class NameDialog : Form
{
    private readonly TextBox _box;

    public string Result { get; private set; } = "";

    public NameDialog()
    {
        Text = "保存场景";
        ClientSize = new Size(400, 176);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        var label = new Label
        {
            Text = "为当前代理状态命名（写入 settings.json）：",
            AutoSize = true,
            ForeColor = Theme.SubText,
            Location = new Point(20, 18)
        };

        _box = new TextBox
        {
            Location = new Point(20, 48),
            Width = 360,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            Font = Theme.Body
        };

        var ok = new PillButton { Text = "保存", Accent = true };
        ok.SurfaceColor = Theme.Bg;
        ok.Location = new Point(214, 104);
        ok.Click += (_, _) =>
        {
            Result = _box.Text.Trim();
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancel = new PillButton { Text = "取消", Outline = true };
        cancel.SurfaceColor = Theme.Bg;
        cancel.Location = new Point(304, 104);
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.Add(label);
        Controls.Add(_box);
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
