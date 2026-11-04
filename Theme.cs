using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClashDock;

/// <summary>统一配色与字体（深色现代风格）。</summary>
public static class Theme
{
    public static readonly Color Bg = Color.FromArgb(21, 23, 29);
    public static readonly Color Card = Color.FromArgb(31, 34, 43);
    public static readonly Color Card2 = Color.FromArgb(39, 43, 54);
    public static readonly Color CardHover = Color.FromArgb(50, 55, 68);
    public static readonly Color Input = Color.FromArgb(26, 29, 37);
    public static readonly Color Border = Color.FromArgb(56, 61, 75);
    public static readonly Color Text = Color.FromArgb(230, 233, 240);
    public static readonly Color SubText = Color.FromArgb(148, 155, 172);
    public static readonly Color Accent = Color.FromArgb(94, 134, 255);
    public static readonly Color AccentHover = Color.FromArgb(122, 156, 255);
    public static readonly Color Ok = Color.FromArgb(74, 201, 143);
    public static readonly Color Warn = Color.FromArgb(240, 180, 60);
    public static readonly Color Err = Color.FromArgb(238, 98, 98);

    public static readonly Font Title = new("Microsoft YaHei UI", 14F, FontStyle.Bold);
    public static readonly Font Sub = new("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
    public static readonly Font Body = new("Microsoft YaHei UI", 9.5F);
    public static readonly Font Small = new("Microsoft YaHei UI", 8.5F);
}

/// <summary>绘图辅助。</summary>
public static class Gfx
{
    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        if (d <= 0 || r.Width <= 0 || r.Height <= 0 || d > Math.Min(r.Width, r.Height))
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
