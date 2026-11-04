using System.Reflection;
using System.Text.Json;
namespace ClashDock;
internal static class DiscoveryChecks
{
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    internal static void Run(string path)
    {
        var checks = new Dictionary<string, bool>();
        var catalog = ApplicationCatalog.Normalize([new("编辑器", @"C:\Apps\Code.exe", "shortcut"), new("duplicate", @"c:\apps\code.EXE", "registry"), new("文档", @"C:\Docs\readme.txt", "fixture")]);
        checks["catalog_deduplicates_executables"] = catalog.Length == 1 && catalog[0].Name == "编辑器";
        var connection = ControllerDiscovery.Parse("external-controller: 127.0.0.1:9099\nsecret: fixture-only\n");
        checks["local_controller_read_without_manual_configuration"] = connection is { Host: "127.0.0.1", Port: 9099, Secret: "fixture-only" };
        checks["wildcard_listener_maps_to_loopback"] = ControllerDiscovery.Parse("external-controller: 0.0.0.0:9097\n")?.Host == "127.0.0.1";
        checks["remote_controller_not_autoselected"] = ControllerDiscovery.Parse("external-controller: remote.example:9097\n") == null;
        checks["missing_controller_keeps_existing_fallback"] = ControllerDiscovery.Parse("mode: rule\n") == null;
        var settings = new Settings { Routing = new RoutingSettings { Applications = [new() { Target = @"C:\Apps\Code.exe", Mode = "直连" }] } };
        using var form = new RoutingForm(new MihomoClient("127.0.0.1", 1, ""), settings, preview: true);
        form.Show(); Application.DoEvents();
        form.MergeApps([catalog[0], new("浏览器", @"C:\Apps\Browser.exe", "fixture")]);
        var grid = Field<DataGridView>(form, "_apps");
        checks["automatic_listing_preserves_saved_mode"] = grid.Rows.Count == 2 && grid.Rows[0].Cells[1].Value?.ToString() == "直连";
        checks["discovery_does_not_mark_dirty"] = !Field<bool>(form, "_dirty");
        var added = grid.Rows.Cast<DataGridViewRow>().Single(r => r.Cells[0].Value?.ToString() == @"C:\Apps\Browser.exe");
        checks["new_discovery_defaults_to_automatic"] = added.Cells[1].Value?.ToString() == "自动";
        var entries = typeof(RoutingForm).GetMethod("Entries", BindingFlags.Instance | BindingFlags.NonPublic)!;
        List<RouteEntry> Saved() => (List<RouteEntry>)entries.Invoke(form, [grid])!;
        checks["untouched_discoveries_not_persisted_as_rules"] = Saved().Count == 1;
        added.Cells[1].Value = "代理";
        form.MergeApps([new("浏览器", @"C:\Apps\Browser.exe", "fixture")]);
        checks["rescan_preserves_unsaved_edits_and_deduplicates"] = grid.Rows.Count == 2 && added.Cells[1].Value?.ToString() == "代理";
        Field<TextBox>(form, "_appSearch").Text = "浏览";
        checks["friendly_name_search"] = grid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible) == 1;
        checks["filtered_out_rules_still_saved"] = Saved().Count == 2;
        added.Cells[1].Value = "自动";
        checks["reset_discovered_app_removes_override"] = Saved().Count == 1;
        checks["settings_not_changed_before_apply"] = settings.Routing.Applications.Count == 1 && settings.Routing.Applications[0].Mode == "直连";
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { ApplicationCatalog.ScanAsync(cancel.Token).GetAwaiter().GetResult(); checks["cancelled_scan"] = false; } catch (OperationCanceledException) { checks["cancelled_scan"] = true; }
        form.Hide();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = checks.Values.All(x => x) ? 0 : 1;
    }
    internal static void Probe(string path)
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var apps = ApplicationCatalog.ScanAsync(cancel.Token).GetAwaiter().GetResult();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { Count = apps.Length, Sources = apps.GroupBy(x => x.Source).ToDictionary(g => g.Key, g => g.Count()) }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
