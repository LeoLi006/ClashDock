using System.Text.Json;
namespace ClashDock;
internal static class UiPreview
{
    static IEnumerable<Control> Descendants(Control root) { foreach (Control c in root.Controls) { yield return c; foreach (var child in Descendants(c)) yield return child; } }
    internal static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var draft = new Settings { Routing = new RoutingSettings { Applications = [new() { Target = @"C:\Apps\Code.exe", Mode = "代理" }, new() { Target = @"C:\Apps\WeChat.exe", Mode = "直连" }, new() { Target = @"C:\Apps\CloudMusic.exe", Mode = "自动" }], Websites = [new() { Target = "github.com", Mode = "代理" }, new() { Target = "bilibili.com", Mode = "直连" }] } };
        using var form = new RoutingForm(new MihomoClient("127.0.0.1", 1, ""), draft, preview: true);
        form.Show(); Application.DoEvents();
        form.MergeApps([new("Visual Studio Code", @"C:\Apps\Code.exe", "示例应用"), new("微信", @"C:\Apps\WeChat.exe", "示例应用"), new("网易云音乐", @"C:\Apps\CloudMusic.exe", "示例应用")]);
        var checks = new Dictionary<string, bool>();
        var apply = Descendants(form).OfType<Button>().Single(b => b.Text == "保存并应用");
        foreach (var size in new[] { new Size(1020, 760), new Size(920, 720), new Size(1280, 850) })
        {
            form.ClientSize = size; Application.DoEvents();
            var buttonRect = apply.RectangleToScreen(apply.ClientRectangle);
            var tun = Descendants(form).OfType<PillButton>().Single(b => b.Text.Contains("TUN"));
            checks[$"tun_visible_{size.Width}"] = form.RectangleToScreen(form.ClientRectangle).Contains(tun.RectangleToScreen(tun.ClientRectangle));
            checks[$"apply_visible_{size.Width}"] = form.RectangleToScreen(form.ClientRectangle).Contains(buttonRect);
            foreach (var section in new[] { "应用规则", "网站规则", "设置与说明" })
            {
                var nav = Descendants(form).OfType<PillButton>().First(b => b.Text == section); nav.PerformClick(); Application.DoEvents();
                if (section == "应用规则")
                {
                    var grid = Descendants(form).OfType<DataGridView>().First(g => g.Visible);
                    checks[$"grid_visible_{size.Width}"] = grid.Width > 280 && grid.Height > 100;
                    var status = Descendants(form).OfType<Label>().Single(l => l.Text.Contains("界面预览"));
                    checks[$"footer_no_overlap_{size.Width}"] = status.RectangleToScreen(status.ClientRectangle).Right <= buttonRect.Left;
                }
                if (size.Width == 1020) { using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(directory, section + ".png")); }
            }
        }
        using var dialog = new TextEntryDialog("添加网站", "输入域名或粘贴网址"); dialog.Show(form); Application.DoEvents();
        using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(directory, "添加网站.png")); }
        dialog.Close(); form.Hide();
        File.WriteAllText(Path.Combine(directory, "ui-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = checks.Values.All(x => x) ? 0 : 1;
    }
}
