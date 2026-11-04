namespace ClashDock;
internal static class AdvancedConnection
{
    internal static void Show(IWin32Window owner, Settings settings)
    {
        using var form = new Form { Text = "高级连接配置（自动识别失败时使用）", ClientSize = new Size(680, 390), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Padding = new Padding(24) };
        UiStyle.Window(form);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        var manual = new CheckBox { Text = "手动连接（关闭则自动读取 Verge 配置）", Checked = settings.UseManualController, AutoSize = true };
        var host = new TextBox { Width = 580, Text = settings.Host };
        var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Math.Clamp(settings.Port, 1, 65535), Width = 180, BackColor = Theme.Input, ForeColor = Theme.Text };
        var secret = new TextBox { Width = 580, Text = settings.Secret, UseSystemPasswordChar = true };
        UiStyle.Input(host); UiStyle.Input(secret);
        flow.Controls.Add(manual);
        foreach (var (title, control) in new[] { ("本机地址", (Control)host), ("控制器端口", port), ("控制器密钥", secret) })
        { flow.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Theme.SubText }); flow.Controls.Add(control); }
        void Enable() { host.Enabled = port.Enabled = secret.Enabled = manual.Checked; }
        manual.CheckedChanged += (_, _) => Enable(); Enable();
        var save = new PillButton { Text = "保存连接（重启工具后使用）", Accent = true, Margin = new Padding(0, 16, 0, 0) };
        save.Click += (_, _) =>
        {
            string hostname = host.Text.Trim();
            if (manual.Checked && hostname is not ("localhost" or "127.0.0.1" or "::1" or "[::1]")) { MessageBox.Show(form, "仅支持本机控制器地址。", "连接配置"); return; }
            var before = (settings.UseManualController, settings.Host, settings.Port, settings.Secret);
            try
            {
                settings.UseManualController = manual.Checked;
                if (manual.Checked) { settings.Host = hostname == "::1" ? "[::1]" : hostname; settings.Port = (int)port.Value; settings.Secret = secret.Text; }
                settings.SaveChecked(); form.DialogResult = DialogResult.OK;
            }
            catch { (settings.UseManualController, settings.Host, settings.Port, settings.Secret) = before; MessageBox.Show(form, "连接配置保存失败，已保留原配置。", "连接配置"); }
        };
        flow.Controls.Add(save); form.Controls.Add(flow); form.ShowDialog(owner);
    }
}
