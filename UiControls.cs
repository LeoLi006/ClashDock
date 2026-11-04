using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ClashDock;

/// <summary>深色右键/下拉菜单渲染器。</summary>
public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item!.Enabled ? Theme.Text : Theme.SubText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.SubText;
        base.OnRenderArrow(e);
    }
}

public sealed class DarkColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Card;
    public override Color ImageMarginGradientBegin => Theme.Card;
    public override Color ImageMarginGradientMiddle => Theme.Card;
    public override Color ImageMarginGradientEnd => Theme.Card;
    public override Color MenuBorder => Theme.Border;
    public override Color MenuItemBorder => Theme.CardHover;
    public override Color MenuItemSelected => Theme.CardHover;
    public override Color MenuItemSelectedGradientBegin => Theme.CardHover;
    public override Color MenuItemSelectedGradientEnd => Theme.CardHover;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Border;
}

/// <summary>圆角药丸按钮：多种样式，带悬停反馈。</summary>
public sealed class PillButton : Button
{
    /// <summary>是否使用主题强调色填充（实心）样式。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Accent { get; set; }

    /// <summary>是否为描边（透明底）样式。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Outline { get; set; }

    /// <summary>按钮四角所贴合的背景色（用于自绘圆角）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceColor { get; set; } = Theme.Bg;

    private bool _hot;
    private bool _down;

    public PillButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Theme.Body;
        ForeColor = Theme.Text;
        AutoSize = true;
        Height = 34;
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var sz = TextRenderer.MeasureText(Text, Font);
        return new Size(sz.Width + 34, Math.Max(34, sz.Height + 14));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var sb = new SolidBrush(SurfaceColor))
            e.Graphics.FillRectangle(sb, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Gfx.Rounded(r, Height / 2);

        Color fill, border, text;
        if (Accent)
        {
            fill = _hot ? Theme.AccentHover : Theme.Accent;
            border = Color.Transparent;
            text = Color.White;
        }
        else if (Outline)
        {
            fill = _hot ? Theme.CardHover : Color.Transparent;
            border = _hot ? Theme.Accent : Theme.Border;
            text = Theme.Text;
        }
        else
        {
            fill = _hot ? Theme.CardHover : Theme.Card2;
            border = Color.Transparent;
            text = Theme.Text;
        }

        if (fill.A > 0)
        {
            using var b = new SolidBrush(fill);
            e.Graphics.FillPath(b, path);
        }
        if (border.A > 0)
        {
            using var p = new Pen(border, 1f);
            e.Graphics.DrawPath(p, path);
        }
        if (_down)
        {
            using var ov = new SolidBrush(Color.FromArgb(36, 0, 0, 0));
            e.Graphics.FillPath(ov, path);
        }

        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
}

/// <summary>圆角卡片容器（带标题/副标题）。</summary>
public sealed class CardPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; } = "";

    public CardPanel()
    {
        DoubleBuffered = true;
        Padding = Padding.Empty;
    }

    /// <summary>内容（子控件）应放置的顶部 Y 坐标：按标题/副标题的实测高度动态计算。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ContentTop
    {
        get
        {
            int y = 18;
            if (!string.IsNullOrEmpty(Title))
                y += TextRenderer.MeasureText(Title, Theme.Sub).Height + 6;
            if (!string.IsNullOrEmpty(Subtitle))
                y += TextRenderer.MeasureText(Subtitle, Theme.Small).Height + 14;
            return y;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Gfx.Rounded(r, 12))
        {
            using var b = new SolidBrush(Theme.Card);
            e.Graphics.FillPath(b, path);
            using var pen = new Pen(Theme.Border, 1f);
            e.Graphics.DrawPath(pen, path);
        }

        int y = 18;
        if (!string.IsNullOrEmpty(Title))
        {
            int h = TextRenderer.MeasureText(Title, Theme.Sub).Height;
            TextRenderer.DrawText(e.Graphics, Title, Theme.Sub, new Point(20, y), Theme.Text);
            y += h + 6;
        }
        if (!string.IsNullOrEmpty(Subtitle))
            TextRenderer.DrawText(e.Graphics, Subtitle, Theme.Small, new Point(21, y), Theme.SubText);

        base.OnPaint(e);
    }
}

/// <summary>现代自绘下拉选择框（深色、圆角、弹出列表）。</summary>
public sealed class ModernSelect : Control
{
    private readonly List<string> _items = new();
    private int _sel = -1;
    private bool _open;
    private bool _hot;
    private ToolStripDropDown? _dd;
    private ListBox? _list;

    public event EventHandler? SelectedIndexChanged;

    public ModernSelect()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Font = Theme.Body;
        Width = 200;
        Height = Math.Max(36, TextRenderer.MeasureText("Wg", Font).Height + 18);
        ForeColor = Theme.Text;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<string> Items => _items;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _sel;
        set
        {
            if (value == _sel)
                return;
            _sel = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? SelectedItem
    {
        get => _sel >= 0 && _sel < _items.Count ? _items[_sel] : null;
        set
        {
            var i = value is null ? -1 : _items.FindIndex(x => x == value);
            _sel = i;
            Invalidate();
        }
    }

    /// <summary>整体填充数据但不触发事件（用于刷新）。</summary>
    public void SetItems(IEnumerable<string> items, string? select)
    {
        _items.Clear();
        _items.AddRange(items);
        _sel = select is null ? -1 : _items.FindIndex(x => string.Equals(x, select, StringComparison.Ordinal));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var sb = new SolidBrush(Parent?.BackColor ?? Theme.Card))
            e.Graphics.FillRectangle(sb, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Gfx.Rounded(r, 8);

        using (var b = new SolidBrush(_hot || _open ? Theme.Card2 : Theme.Input))
            e.Graphics.FillPath(b, path);
        using (var pen = new Pen(_open || Focused ? Theme.Accent : Theme.Border, 1f))
            e.Graphics.DrawPath(pen, path);

        var text = SelectedItem ?? "—";
        var color = SelectedItem is null ? Theme.SubText : Theme.Text;
        TextRenderer.DrawText(e.Graphics, text, Font,
            new Rectangle(14, 0, Width - 44, Height), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // 下拉箭头
        int cx = Width - 20, cy = Height / 2 - 1;
        using var arrow = new Pen(Theme.SubText, 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        e.Graphics.DrawLines(arrow, new[]
        {
            new Point(cx - 4, cy - 2),
            new Point(cx, cy + 2),
            new Point(cx + 4, cy - 2)
        });
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left)
            ToggleDropDown();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
            ToggleDropDown();
        else if (e.KeyCode == Keys.Down && _items.Count > 0)
            SelectedIndex = Math.Min(_sel + 1, _items.Count - 1);
        else if (e.KeyCode == Keys.Up && _items.Count > 0)
            SelectedIndex = Math.Max(_sel - 1, 0);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    private void ToggleDropDown()
    {
        if (_open)
        {
            _dd?.Close();
            return;
        }
        if (_items.Count == 0)
            return;
        EnsureDropDown();

        _list!.Items.Clear();
        _list.Items.AddRange(_items.ToArray());
        _list.SelectedIndex = _sel;

        int w = Math.Max(Width, 180);
        int h = Math.Min(300, _items.Count * 26 + 4);
        _list.Size = new Size(w - 2, h - 2);
        if (_dd!.Items.Count > 0)
            _dd.Items[0].Size = _list.Size;
        _dd.Size = new Size(w, h);

        _open = true;
        Invalidate();
        _dd.Show(this, new Point(0, Height + 3));
    }

    private void EnsureDropDown()
    {
        if (_dd is not null)
            return;

        _list = new ListBox
        {
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 26,
            IntegralHeight = false,
            BackColor = Theme.Card2,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            TabStop = false
        };
        _list.DrawItem += OnDrawItem;
        _list.MouseDown += (_, e) =>
        {
            int i = _list.IndexFromPoint(e.Location);
            if (i >= 0)
            {
                _list.SelectedIndex = i;
                Commit();
            }
        };

        _dd = new ToolStripDropDown
        {
            AutoSize = false,
            Padding = Padding.Empty,
            BackColor = Theme.Card2,
            DropShadowEnabled = true
        };
        _dd.Closed += (_, _) =>
        {
            _open = false;
            Invalidate();
        };

        var host = new ToolStripControlHost(_list)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _dd.Items.Add(host);
    }

    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _items.Count)
            return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(selected ? Theme.CardHover : Theme.Card2))
            e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, _items[e.Index], Font,
            new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height),
            e.Index == _sel ? Theme.Accent : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void Commit()
    {
        var i = _list!.SelectedIndex;
        _dd!.Close();
        if (i >= 0 && i != _sel)
            SelectedIndex = i; // 触发事件
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _dd?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>策略组行：左侧色条 + 组名/当前值 + 右侧现代下拉。</summary>
public sealed class GroupRow : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string GroupName { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernSelect Selector { get; } = new();

    public GroupRow()
    {
        DoubleBuffered = true;
        Height = 60;
        Margin = new Padding(0, 0, 0, 8);
        Selector.Width = 240;
        Controls.Add(Selector);
    }

    /// <summary>按文字实测高度自适应行高，避免高 DPI 下组名与当前值重叠。</summary>
    public void FitToContent()
    {
        int nameH = TextRenderer.MeasureText(GroupName, Theme.Sub).Height;
        int valH = TextRenderer.MeasureText(Value, Theme.Small).Height;
        int content = nameH + 6 + valH;
        Height = Math.Max(Selector.Height + 26, content + 28);
        Selector.Location = new Point(Width - Selector.Width - 20, (Height - Selector.Height) / 2);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Selector.Location = new Point(Width - Selector.Width - 20, (Height - Selector.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Gfx.Rounded(r, 10))
        {
            using var b = new SolidBrush(Theme.Card);
            e.Graphics.FillPath(b, path);
            using var pen = new Pen(Theme.Border, 1f);
            e.Graphics.DrawPath(pen, path);
        }

        bool direct = Value.Equals("DIRECT", StringComparison.OrdinalIgnoreCase)
                      || Value.Contains("直连", StringComparison.Ordinal);
        var accent = direct ? Theme.Ok : Theme.Accent;

        int nameH = TextRenderer.MeasureText(GroupName, Theme.Sub).Height;
        int valH = TextRenderer.MeasureText(Value, Theme.Small).Height;
        int total = nameH + 6 + valH;
        int top = Math.Max(8, (Height - total) / 2);

        using (var bar = new SolidBrush(accent))
        using (var barPath = Gfx.Rounded(new Rectangle(14, top + 2, 4, total - 4), 2))
            e.Graphics.FillPath(bar, barPath);

        int textW = Math.Max(40, Width - Selector.Width - 56);
        var nameRect = new Rectangle(32, top, textW, nameH);
        var valRect = new Rectangle(32, top + nameH + 6, textW, valH);
        TextRenderer.DrawText(e.Graphics, GroupName, Theme.Sub, nameRect, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, Value, Theme.Small, valRect,
            direct ? Theme.Ok : Theme.SubText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        base.OnPaint(e);
    }
}

/// <summary>双缓冲滚动容器（消除 AutoScroll 闪烁）。</summary>
public sealed class ScrollPanel : Panel
{
    public ScrollPanel()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Bg);
    }
}

/// <summary>自动换行的药丸按钮容器：按内容与实际宽度计算高度。</summary>
public sealed class PillFlow : Panel
{
    /// <summary>按钮间距。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Gap { get; set; } = 10;

    public PillFlow()
    {
        DoubleBuffered = true;
        BackColor = Theme.Card;
    }

    /// <summary>按当前宽度重排子控件并更新自身高度。</summary>
    public void Relayout()
    {
        int avail = Math.Max(60, Width);
        int x = 0, y = 0, lineH = 0;
        foreach (Control c in Controls)
        {
            if (x > 0 && x + c.Width > avail)
            {
                x = 0;
                y += lineH + Gap;
                lineH = 0;
            }
            c.Location = new Point(x, y);
            x += c.Width + Gap;
            lineH = Math.Max(lineH, c.Height);
        }
        Height = Math.Max(1, y + lineH);
    }
}
