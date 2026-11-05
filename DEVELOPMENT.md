# 实现与验证

## 责任划分

- `ApplicationCatalog`：后台 STA 应用发现，受生命周期取消；图标缓存由窗口释放。
- `RoutingCompiler`：域名规范化、网站/子域优先、默认直连排除明确自动应用；保留订阅规则的相对顺序。
- `RoutingService`：全局串行网络门；校验源配置一致性，备份并原子替换托管脚本，再通过 Mihomo API 应用、读回验证。失败有界回滚并报告是否确认恢复。沿用当前端口及 TUN 等运行字段，未提供 TUN 管理入口。
- `AutoRouting`：UI 线程所有的意图、版本、防抖、单 worker。UI 编辑持续可用，新修改不被旧结果标成成功。持久意图与已生效 Settings 分开；无自动重试风暴。
- `SingleInstance`：按用户和安装目录隔离命名互斥量、限当前用户命名管道。正常启动显示，`--silent` 托盘启动；唤起恢复最小化及隐藏状态。
- `TrayApp`：统一队列、窗口、托盘生命周期和 12 秒只读核验。重建菜单释放旧菜单；关闭主窗口不销毁队列。
- `Telemetry`：已有延迟历史、手动异步测速（全局并发 2）、只读连接诊断。正文不持久化。

旧 settings.json 中场景、强制模式等字段不再使用，反序列化兼容忽略，不在初次加载自动重写。待用户修改时写入当前格式。暂停设置和恢复配置保留。文件路径、控制器密钥、意图及备份不进 Git。

## 边界

私有组以 `__ClashDock_` 开头，脚本仅管理明确 Begin/End 区块。原始 `main` 包装后执行，不改写用户函数。用户脚本与保留变量冲突、脚本标记损坏、生成 YAML 与运行订阅不同则拒绝覆盖。自动恢复只包含一次有限回滚，失败不能宣称成功；配置被外部覆盖仅提示手动修复。

规则结构不变且私有 Selector 支持新目标时，切目标只更新脚本与 Selector，不重载整份 YAML；结构变更或覆盖恢复用已有事务路径。读取的候选目标包含提供器中的实际节点。

## 检查命令

```powershell
dotnet build tests/ClashDock.Checks.csproj -c Release
.\tests\bin\Release\net9.0-windows\win-x64\ClashDock.Checks.exe
.\tests\InstanceChecks.ps1
.\artifacts\ui-build\ClashDock.exe --preview-ui artifacts/ui-current
.\artifacts\ui-build\ClashDock.exe --discovery-check artifacts/discovery-check.json
.\artifacts\ui-build\ClashDock.exe --live-check artifacts/live-check.json
```

前三类使用隔离示例；测试 worker 注入 HttpMessageHandler，不把配置提交给真实控制器。`--live-check` 仅 GET `/configs`、`/rules`、`/proxies`，报告数量和错误类型，不保存名称或密钥。它不会测试节点出口，不会改系统代理或 TUN。

具体本轮检查结果见本地 `artifacts` 报告和 [验证结果](VALIDATION.md)。短测不等于多天稳定性或真实分流效果认证。
