using System.Text.Json;

namespace ClashDock;

/// <summary>
/// 工具配置。首次运行会在程序目录生成 settings.json，
/// 可在其中修改控制器地址、密钥、是否开机强制规则模式等。
/// </summary>
public sealed class Settings
{
    public bool UseManualController { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 9097;
    public string Secret { get; set; } = "set-your-secret";

    /// <summary>旧配置兼容字段；当前版本不在启动时改网络模式，始终需用户确认。</summary>
    public bool ForceRuleModeOnStart { get; set; } = true;

    /// <summary>弹出面板的全局热键，格式如 Ctrl+Alt+Shift+F10。</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+Shift+F10";

    /// <summary>
    /// 场景预设：托盘菜单/面板中一键套用的组合（切模式 + 批量切多个策略组）。
    /// 可直接编辑 settings.json 增删改，或使用「保存当前状态为场景」生成。
    /// </summary>
    public List<Preset> Presets { get; set; } = new();
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
                    loaded.Presets ??= new();
                    loaded.Routing ??= new();
                    loaded.Routing.Applications ??= new();
                    loaded.Routing.Websites ??= new();
                    loaded.Routing.AppliedRules ??= new();
                    if (loaded.Presets.Count == 0)
                    {
                        loaded.Presets = Preset.Defaults();
                        loaded.Save();
                    }
                    return loaded;
                }
            }
        }
        catch
        {
            // 配置损坏时回退默认值
        }

        var settings = new Settings { Presets = Preset.Defaults() };
        settings.Save();
        return settings;
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // 忽略写入失败（例如只读目录）
        }
    }

    public void SaveChecked()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, FilePath, true);
    }
}
