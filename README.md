# ClashDock 1.2.0

独立的 Windows 应用／网站分流工具，连接本机 Clash Verge / Mihomo。**不管理 TUN，不强制接管软件，不集成 Leo。**

## 快速开始

1. 运行 `publish/ClashDock.exe`，普通启动直接显示工作台；重复运行唤起已有窗口。
2. 保持 Verge 运行并启用本机外部控制器。工具自动读取连接配置，识别失败再用高级设置。
3. 选择代理目标，在应用或网站旁选择 **自动／直连／代理**。修改约 450 ms 后自动保存并串行应用；无需保存按钮。
4. 应用名旁星标可置顶；右键也可置顶，重扫和重启保留，搜索仍筛选全部应用。置顶只保存界面偏好。
5. 确认底部状态为“已生效”。失败保留修改，点“重试应用”；点击状态可查看完整原因。

应用需要遵循系统代理或配置 Clash 代理。**没有进入 Clash 的流量不受规则控制。** 已有连接可能需要刷新页面或重开应用。网站规则优先于应用规则，子域名优先，同域名标签页共用设置。

## 使用与维护

- 关闭窗口 / Esc：回到托盘，后台完成排队修改。
- 托盘：打开主界面、暂停／恢复分流、退出。
- **退出保留已安装规则**；暂停仅撤下 ClashDock 托管规则，保留订阅和用户脚本。退出前等待队列；失败意图落盘，下次启动需手动重试，不自动争抢配置。
- 静默启动：`ClashDock.exe --silent`，供开机自启使用。本轮不自动修改启动项。
- 代理目标框及下拉直接显示名称、路径与延迟/状态；与“目标状态”窗口共用缓存。“测速 / 刷新”中可读取历史、测当前、全部测速，全局并发上限 2。显示时间与陈旧提示，无历史不会当成超时。节点身份和显示文字分开，刷新测速不切换目标或应用规则。
- 选中应用／网站 → “检查分流”：先只读核验控制器、意图、暂停、模式和托管规则，再可观察 15 秒新连接。原始进程/域名/规则/链路放在可展开详情；缺路径仅标注名称候选，没有记录仅说明“尚未检测到”。链路不是公网出口 IP。
- 常用站点模板可先编辑域名列表，包含 B 站资源域名；双击已有网站名称修改域名。
- “设置与说明”：兜底行为、Verge 目录、高级连接、撤销上次修改、移除全部托管规则。

## Edge 扩展

发布包包含 `EdgeExtension`、`ClashDock.Bridge.exe` 和 `BridgeTools`，**不会自动安装或注册**。按 [扩展安装指南](EDGE_EXTENSION.md) 手动进行当前用户注册，再在 Edge 加载扩展。点击工具栏即可操作当前域名及子域名的自动/直连/代理，跟随桌面全局目标，同一份规则和队列，无每网站独立节点、无浏览器全局代理权限。

扩展失败时保留明确状态，桌面未运行可显式启动；模板先预览域名再确认，刷新网页由用户点击。校园网认证不在本轮范围内。

详细操作和状态解释见 [使用说明](使用说明.md)。实现、验证与限制见 [开发与验证说明](DEVELOPMENT.md)。

## 构建

Windows、.NET 9 SDK：

```powershell
dotnet build ClashDock.csproj -c Release
dotnet publish ClashDock.csproj -c Release -o artifacts/ui-build
dotnet publish Bridge/ClashDock.Bridge.csproj -c Release -o artifacts/bridge-build
dotnet build tests/ClashDock.Checks.csproj -c Release
.\tests\bin\Release\net9.0-windows\win-x64\ClashDock.Checks.exe
node tests/ExtensionChecks.cjs
```

桌面端和通信桥分别是 win-x64 自包含单文件，无需另装 .NET。保留 EXE 同目录个人 `settings.json`、`routing-intent.json` 、撤销记录与 `ui-preferences.json`；这些文件不提交 Git。直接替换 EXE 前退出旧程序。

## 第三方与协议

YAML 处理复用 YamlDotNet 16.3.0（MIT）；.NET 运行时许可证随本地发布目录附带。见 [第三方声明](THIRD_PARTY_NOTICES.md)。控制器交互依据 [Mihomo 官方 API](https://wiki.metacubex.one/api/)，不嵌入 Mihomo 或 Verge 可执行代码，不自动下载依赖。
