# ClashDock 1.2.0

独立的 Windows 应用／网站分流工具，连接本机 Clash Verge / Mihomo。**不管理 TUN，不强制接管软件，不集成 Leo。**

## 当前更新

本次 `main` 更新集中完善自动分流、Edge 联动和可靠性：

- 应用自动发现、名称／图标展示、搜索和置顶；目标延迟与测速状态共用缓存。
- 选择自动保存、防抖串行应用；区分保存意图、规则核验和连接观察。
- Edge 扩展管理当前域名及子域名，直连显示“预期出口”；节点详情折叠。
- 重新应用全部分流需先预览规则数量及影响范围；失败保留选择，不盲目重放。
- 桌面内置通信桥检查／当前用户注册；兼容 PowerShell 5.1／7 和中文路径。
- 修复 IPv6 类型别名、GEOIP 国家代码大小写及错误分类误判。

完整记录见 [更新日志](CHANGELOG.md)，测试边界见 [验证记录](VALIDATION.md)。

**仓库源码不包含本地 EXE、个人配置或通信桥注册清单。** `publish/` 是构建输出目录；下载源码后请按下文构建。GitHub Release 与 `main` 源码分别维护，旧发布包可能不含上述更新。

## 快速开始

1. 运行 `publish/ClashDock.exe`，普通启动直接显示工作台；重复运行唤起已有窗口。
2. 保持 Verge 运行并启用本机外部控制器。工具自动读取连接配置，识别失败再用高级设置。
3. 选择代理目标，在应用或网站旁选择 **自动／直连／代理**。修改约 450 ms 后自动保存并串行应用；无需保存按钮。
4. 应用名旁星标可置顶；右键也可置顶，重扫和重启保留，搜索仍筛选全部应用。置顶只保存界面偏好。
5. 按钮表示保存选择；底部“规则已加载”只说明内核核验通过，不代表实际出口已验证。失败保留修改；点“重新应用全部分流”先查看全部应用／网站数量与影响范围，再确认。点击状态可查看完整原因。

应用需要遵循系统代理或配置 Clash 代理。**没有进入 Clash 的流量不受规则控制。** 已有连接可能需要刷新页面或重开应用。网站规则优先于应用规则，子域名优先，同域名标签页共用设置。

全局模式不会自动切换为规则模式：请在 Verge 自行核对。遇到模式不参与分流、目标失效、规则变化或保存失败，状态会给出不同处理建议，不自动争抢配置。

## 使用与维护

- 关闭窗口 / Esc：回到托盘，后台完成排队修改。
- 托盘：打开主界面、暂停／恢复分流、退出。
- **退出保留已安装规则**；暂停仅撤下 ClashDock 托管规则，保留订阅和用户脚本。退出前等待队列；失败意图落盘，下次启动需手动重试，不自动争抢配置。
- 静默启动：`ClashDock.exe --silent`，供开机自启使用，不自动修改启动项。
- 代理目标框及下拉直接显示名称、路径与延迟/状态；与“目标状态”窗口共用缓存。“测速 / 刷新”中可读取历史、测当前、全部测速，全局并发上限 2。显示时间与陈旧提示，无历史不会当成超时。节点身份和显示文字分开，刷新测速不切换目标或应用规则。
- 选中应用／网站 → “检查分流”：先只读核验控制器、意图、暂停、模式和托管规则，再可观察 15 秒新连接。原始进程/域名/规则/链路放在可展开详情；缺路径仅标注名称候选，没有记录仅说明“尚未检测到”。链路不是公网出口 IP。
- 常用站点模板可先编辑域名列表，包含 B 站资源域名；双击已有网站名称修改域名。
- “设置与说明”：兜底行为、Verge 目录、高级连接、通信桥检查／当前安装注册、撤销上次修改、移除全部托管规则。

## Edge 扩展

发布包包含 `EdgeExtension`、`ClashDock.Bridge.exe` 和 `BridgeTools`，**不会自动安装或注册**。可在桌面「设置与说明 → Edge 通信桥检查／注册」检查并注册当前用户，也可按 [扩展安装指南](EDGE_EXTENSION.md) 使用脚本；扩展仍由用户在 Edge 加载。点击工具栏即可操作当前域名及子域名的自动/直连/代理，跟随桌面全局目标，同一份规则和队列，无每网站独立节点、无浏览器全局代理权限。

扩展失败时保留明确状态，桌面未运行可显式启动；模板先预览域名再确认，刷新网页由用户点击。校园网认证不在本轮范围内。

详细操作和状态解释见 [使用说明](使用说明.md)。实现、验证与限制见 [开发与验证说明](DEVELOPMENT.md)。

## 构建

Windows、.NET 9 SDK：

```powershell
dotnet publish ClashDock.csproj -c Release -o publish
dotnet publish Bridge/ClashDock.Bridge.csproj -c Release -o publish
Copy-Item EdgeExtension, BridgeTools -Destination publish -Recurse -Force
.\publish\ClashDock.exe
```

保持两个 EXE 与 `EdgeExtension/`、`BridgeTools/` 相邻。首次扩展安装按 [安装指南](EDGE_EXTENSION.md) 完成。已有安装更新前先退出程序，保留其个人配置。

本轮直接相关的开发检查：

```powershell
dotnet build tests/ClashDock.Checks.csproj -c Release
.\tests\bin\Release\net9.0-windows\win-x64\ClashDock.Checks.exe --subscription-only
.\tests\bin\Release\net9.0-windows\win-x64\ClashDock.Checks.exe --reliability-only
node tests/ExtensionChecks.cjs
node tests/RenderPopup.cjs
```

桌面端和通信桥分别是 win-x64 自包含单文件，无需另装 .NET。保留 EXE 同目录个人 `settings.json`、`routing-intent.json` 、撤销记录与 `ui-preferences.json`；这些文件不提交 Git。直接替换 EXE 前退出旧程序。

## 第三方与协议

YAML 处理复用 YamlDotNet 16.3.0（MIT）；.NET 运行时许可证随本地发布目录附带。见 [第三方声明](THIRD_PARTY_NOTICES.md)。控制器交互依据 [Mihomo 官方 API](https://wiki.metacubex.one/api/)，不嵌入 Mihomo 或 Verge 可执行代码，不自动下载依赖。
