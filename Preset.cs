namespace ClashDock;

/// <summary>一条场景动作：把「组名包含 GroupKeyword 的策略组」切换为 Option。</summary>
public sealed class PresetAction
{
    /// <summary>策略组关键字（用包含匹配，换订阅后只要组名大致相同仍然有效）。</summary>
    public string GroupKeyword { get; set; } = "";

    /// <summary>目标选项，例如 DIRECT 或某个节点名。为空表示该动作不切换。</summary>
    public string Option { get; set; } = "";
}

/// <summary>
/// 场景预设：一次点击 = 设定运行模式 + 批量切换多个策略组。
/// 用于把「B站直连 / 外网走代理」等常用组合固化成一键操作。
/// </summary>
public sealed class Preset
{
    public string Name { get; set; } = "";

    /// <summary>可选：同时切到的运行模式（rule/global/direct）。空字符串表示不改模式。</summary>
    public string Mode { get; set; } = "";

    public List<PresetAction> Actions { get; set; } = new();

    /// <summary>内置默认场景（首次运行写入 settings.json，可自行增删改）。</summary>
    public static List<Preset> Defaults() => new()
    {
        new Preset
        {
            Name = "🎬 国内直连(含B站)",
            Mode = "rule",
            Actions =
            {
                new PresetAction { GroupKeyword = "哔哩哔哩", Option = "DIRECT" },
                new PresetAction { GroupKeyword = "国内网站", Option = "DIRECT" },
            }
        },
        new Preset
        {
            Name = "🤖 外网走代理(ChatGPT等)",
            Mode = "rule",
            Actions =
            {
                // 仅在组存在且选项存在时才切换；找不到就跳过，不会报错。
                new PresetAction { GroupKeyword = "选择节点", Option = "" },
                new PresetAction { GroupKeyword = "漏网之鱼", Option = "" },
            }
        },
        new Preset { Name = "🌐 全局代理", Mode = "global" },
        new Preset { Name = "🚫 全部直连", Mode = "direct" },
    };
}

/// <summary>场景应用引擎（托盘菜单与可视化面板共用）。</summary>
public static class PresetEngine
{
    /// <summary>
    /// 应用场景：先切模式，再逐条匹配策略组并切换选项。
    /// 采用「关键字包含 + 选项存在才切换」的宽松策略，跨订阅也不易失败。
    /// </summary>
    public static async Task ApplyAsync(MihomoClient client, Preset preset)
    {
        if (!string.IsNullOrWhiteSpace(preset.Mode))
            await client.SetModeAsync(preset.Mode);

        if (preset.Actions.Count == 0)
            return;

        var groups = await client.GetSelectorGroupsAsync();
        foreach (var act in preset.Actions)
        {
            if (string.IsNullOrWhiteSpace(act.Option))
                continue;

            var g = groups.FirstOrDefault(x =>
                x.Name.Contains(act.GroupKeyword, StringComparison.OrdinalIgnoreCase));
            if (g is null)
                continue;

            var opt = g.Options.FirstOrDefault(o =>
                string.Equals(o, act.Option, StringComparison.OrdinalIgnoreCase));
            if (opt is null)
                continue;

            await client.SelectAsync(g.Name, opt);
        }
    }

    /// <summary>把当前所有策略组的选中状态捕获为一个具体场景（用于「保存当前状态」）。</summary>
    public static async Task<Preset> CaptureAsync(MihomoClient client, string name)
    {
        var preset = new Preset
        {
            Name = name,
            Mode = await client.GetModeAsync()
        };

        var groups = await client.GetSelectorGroupsAsync();
        foreach (var g in groups)
        {
            if (!string.IsNullOrEmpty(g.Now))
                preset.Actions.Add(new PresetAction { GroupKeyword = g.Name, Option = g.Now });
        }
        return preset;
    }
}
