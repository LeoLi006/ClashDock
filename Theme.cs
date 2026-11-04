using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClashDock;

/// <summary>统一配色与字体（深色现代风格）。</summary>
public static class Theme
{
    public static readonly Color Bg = Color.FromArgb(17, 21, 28);
    public static readonly Color Card = Color.FromArgb(24, 30, 40);
    public static readonly Color Card2 = Color.FromArgb(31, 39, 51);
    public static readonly Color CardHover = Color.FromArgb(38, 49, 65);
    public static readonly Color Input = Color.FromArgb(19, 25, 34);
    public static readonly Color Border = Color.FromArgb(45, 56, 73);
    public static readonly Color Text = Color.FromArgb(234, 240, 249);
    public static readonly Color SubText = Color.FromArgb(145, 160, 181);
    public static readonly Color Accent = Color.FromArgb(52, 120, 235);
    public static readonly Color AccentHover = Color.FromArgb(76, 143, 255);
    public static readonly Color Ok = Color.FromArgb(77, 202, 163);
    public static readonly Color Warn = Color.FromArgb(236, 186, 100);
    public static readonly Color Err = Color.FromArgb(245, 126, 139);

    public static readonly Font Title = new("Microsoft YaHei UI", 18F, FontStyle.Bold);
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
