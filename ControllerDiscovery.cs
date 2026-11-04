using YamlDotNet.RepresentationModel;
namespace ClashDock;
internal static class ControllerDiscovery
{
    internal static (string Host, int Port, string Secret)? Parse(string yaml)
    {
        var map = RoutingCompiler.ReadYaml(yaml);
        if (!map.Children.TryGetValue(new YamlScalarNode("external-controller"), out var raw)) return null;
        if (!Uri.TryCreate("http://" + raw.ToString(), UriKind.Absolute, out var uri)) return null;
        string host = uri.Host.Trim('[', ']');
        if (host is "0.0.0.0" or "::") host = "127.0.0.1";
        if (host is not ("127.0.0.1" or "localhost" or "::1") || uri.Port < 1 || uri.Port > 65535 || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/") return null;
        return (host == "::1" ? "[::1]" : host, uri.Port, map.Children.TryGetValue(new YamlScalarNode("secret"), out var secret) ? secret.ToString() : "");
    }
    internal static void Apply(Settings settings)
    {
        if (settings.UseManualController) return;
        var directory = string.IsNullOrWhiteSpace(settings.Routing.VergeDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "io.github.clash-verge-rev.clash-verge-rev") : settings.Routing.VergeDirectory;
        try
        {
            var file = Path.Combine(directory, "clash-verge.yaml");
            if (!File.Exists(file)) return;
            var found = Parse(File.ReadAllText(file));
            if (found is { } endpoint) { settings.Host = endpoint.Host; settings.Port = endpoint.Port; settings.Secret = endpoint.Secret; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException or InvalidOperationException) { }
    }
}
