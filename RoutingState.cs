namespace ClashDock;
public sealed class RoutingFailure(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public static string CodeOf(Exception e) => e is RoutingFailure f ? f.Code : e is HttpRequestException or TaskCanceledException ? "controller_offline" : "apply_failed";
}
public sealed record RoutingView(string Code, string Message, string Next, string Reason);
public static class RoutingState
{
    public static RoutingView Describe(AutoRouting engine, Inspection? inspection)
    {
        string phase = engine.StateCode;
        if (phase is "save_failed" or "retry" || engine.FailureCode.Length > 0)
            return new(phase, engine.Status, engine.FailureCode switch {
                "subscription_mismatch" => "在 Verge 重新生成并应用当前订阅，再预览重新应用全部分流。",
                "mode_bypass" => "在 Verge 切换为规则模式后，再预览重新应用全部分流。",
                "target_missing" => "在 ClashDock 主界面重新选择代理目标。",
                "save_failed" => "检查安装目录写入权限；未保存修改仍保留在窗口中。",
                "controller_offline" => "先启动 Verge 并检查高级连接配置，再刷新核验。",
                _ => "在桌面查看完整失败原因；修复后预览重新应用全部分流。"
            }, engine.FailureCode);
        if (phase is "waiting" or "applying" or "saved")
            return new(phase, engine.Status, "按钮表示选择意图；尚未确认最新修改被内核接受。", "");
        if (inspection == null || inspection.Revision != engine.Revision)
            return new("unknown", "尚待核验", "刷新只读状态，不会重新执行修改。", "unverified");
        return new(inspection.ProblemCode, inspection.Conclusion, inspection.Next, inspection.ProblemCode);
    }
    public static (int Apps, int Sites) Counts(RoutingSettings draft) =>
        (draft.Applications.Count(x => x.Mode != "自动"), draft.Websites.Count(x => x.Mode != "自动"));
    public static string RetryScope(RoutingSettings draft, bool paused)
    {
        var (apps, sites) = Counts(draft);
        return $"将重新应用全部分流：{apps} 条应用规则、{sites} 条网站规则；默认行为：{draft.DefaultBehavior}。\n" +
            (paused ? "当前为暂停：只撤下本工具托管规则并保存恢复意图。" : "包括所有已保存的选择，不仅是当前网站；可能重载配置，新连接可能改变路线。") +
            "\n不会修改系统代理或 TUN；已有连接可能仍使用原路线。";
    }
}
