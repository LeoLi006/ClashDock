namespace ClashDock;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--preview-ui") { UiPreview.Run(args[1]); return; }
        if (args.Length == 2 && args[0] == "--discovery-check") { DiscoveryChecks.Run(args[1]); return; }
        if (args.Length == 2 && args[0] == "--scan-apps") { DiscoveryChecks.Probe(args[1]); return; }

        if (args.Length == 2 && args[0] == "--live-check") { LiveCheck.Run(args[1]); return; }
        using var instance = new SingleInstance();
        if (!instance.Primary) { try { instance.Activate(); } catch { MessageBox.Show("已有实例正在启动，请稍后再试。"); } return; }
        using var app = new TrayApp(args.Contains("--silent"));
        instance.Listen(app.RequestOpen);
        Application.Run(app);
    }
}
