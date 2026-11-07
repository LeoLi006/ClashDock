using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

// Local checkout entry. Keep the real application's directory (and configuration) unchanged.
internal static class LocalLauncher
{
    [STAThread]
    private static void Main(string[] args)
    {
        string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "publish");
        string executable = Path.Combine(directory, "ClashDock.exe");
        if (!File.Exists(executable))
        {
            MessageBox.Show("未找到 publish\\ClashDock.exe。请保留完整 ClashDock 文件夹。", "ClashDock");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(executable,
                Array.IndexOf(args, "--silent") >= 0 ? "--silent" : "")
            { WorkingDirectory = directory, UseShellExecute = true });
        }
        catch (Exception)
        {
            MessageBox.Show("无法启动 ClashDock，请检查 publish\\ClashDock.exe 是否可运行。", "ClashDock");
        }
    }
}
