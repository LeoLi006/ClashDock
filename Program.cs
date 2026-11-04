namespace ClashDock;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--preview-ui") { UiPreview.Run(args[1]); return; }
        Application.Run(new TrayApp());
    }
}
