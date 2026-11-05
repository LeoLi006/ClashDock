using System.Text.Json;

namespace ClashDock;

/// <summary>
/// 工具配置。首次运行会在程序目录生成 settings.json，
/// 可在高级设置修改本机连接；旧场景和强制模式字段会被忽略。
/// </summary>
public sealed class Settings
{
    public RoutingSettings? ResumeRouting { get; set; }
    public bool Paused { get; set; }
    public bool UseManualController { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 9097;
    public string Secret { get; set; } = "set-your-secret";

    public RoutingSettings Routing { get; set; } = new();

    private static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<Settings>(text);
                if (loaded is not null)
                {

                    loaded.Routing ??= new();
                    loaded.Routing.Applications ??= new();
                    loaded.Routing.Websites ??= new();
                    loaded.Routing.AppliedRules ??= new();
                    return loaded;
                }
            }
        }
        catch
        {
            // 配置损坏时回退默认值
        }

        var settings = new Settings();
        return settings;
    }

    public void Save() { try { SaveChecked(); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    public void SaveChecked()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, FilePath, true);
    }
}
