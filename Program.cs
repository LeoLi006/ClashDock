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
        if (args.Length == 2 && args[0] == "--network-check") { NetworkChecks.Run(args[1]); return; }
        Application.Run(new TrayApp());
    }
}
