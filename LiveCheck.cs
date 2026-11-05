using System.Text.Json;
namespace ClashDock;
internal static class LiveCheck
{
    internal static void Run(string path)
    {
        var settings = Settings.Load(); ControllerDiscovery.Apply(settings);
        using var client = new MihomoClient(settings.Host,settings.Port,settings.Secret);
        try {
            var network = client.GetNetworkAsync().GetAwaiter().GetResult();
            var rules = client.GetRuleSequenceAsync().GetAwaiter().GetResult();
            var targets = client.GetTargetsAsync().GetAwaiter().GetResult();
            File.WriteAllText(path, JsonSerializer.Serialize(new { connected = true, mode = network.Mode, rules = rules.Length, targets = targets.Count, tested = targets.Count(t=>t.Delay != null), writes = 0 }, new JsonSerializerOptions { WriteIndented = true }));
        } catch (Exception e) { File.WriteAllText(path, JsonSerializer.Serialize(new { connected = false, errorType = e.GetType().Name, writes = 0 })); Environment.ExitCode = 1; }
    }
}
