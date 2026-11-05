# ClashDock

独立的 Windows 应用／网站分流工具，连接本机 Clash Verge / Mihomo。**不管理 TUN，不强制接管软件，不集成 Leo。**

## 快速开始

1. 运行 `publish/ClashDock.exe`，普通启动直接显示工作台；重复运行唤起已有窗口。
2. 保持 Verge 运行并启用本机外部控制器。工具自动读取连接配置，识别失败再用高级设置。
3. 选择代理目标，在应用或网站旁选择 **自动／直连／代理**。修改约 450 ms 后自动保存并串行应用；无需保存按钮。
4. 确认底部状态为“已生效”。失败保留修改，点“重试应用”；点击状态可查看完整原因。

应用需要遵循系统代理或配置 Clash 代理。**没有进入 Clash 的流量不受规则控制。** 已有连接可能需要刷新页面或重开应用。网站规则优先于应用规则，子域名优先，同域名标签页共用设置。

## 使用与维护

- 关闭窗口 / Esc：回到托盘，后台完成排队修改。
- 托盘：打开主界面、暂停／恢复分流、退出。
- **退出保留已安装规则**；暂停仅撤下 ClashDock 托管规则，保留订阅和用户脚本。退出前等待队列；失败意图落盘，下次启动需手动重试，不自动争抢配置。
- 静默启动：`ClashDock.exe --silent`，供开机自启使用。本轮不自动修改启动项。
- “目标状态”：读取已有测速时间、延迟与实际选中节点；可测选中或全部测速。未测速与超时分开，10 分钟前结果标记陈旧；主动测速并发上限 2。
- 选中应用／网站 → “连接诊断”：显示当前连接的进程、域名、命中规则及出口。没有记录仅说明“尚未检测到”。不保存连接正文。
- 常用站点模板可先编辑域名列表，包含 B 站资源域名；双击已有网站名称修改域名。
- “设置与说明”：兜底行为、Verge 目录、高级连接、撤销上次修改、移除全部托管规则。

详细操作和状态解释见 [使用说明](使用说明.md)。实现、验证与限制见 [开发与验证说明](DEVELOPMENT.md)。

## 构建

Windows、.NET 9 SDK：

```powershell
dotnet build ClashDock.csproj -c Release
dotnet publish ClashDock.csproj -c Release -o artifacts/release
dotnet build tests/ClashDock.Checks.csproj -c Release
.\tests\bin\Release\net9.0-windows\win-x64\ClashDock.Checks.exe
```

发布是 win-x64 自包含单文件，无需另装 .NET。保留 EXE 同目录个人 `settings.json`、`routing-intent.json` 与撤销记录；这些文件不提交 Git。直接替换 EXE 前退出旧程序。

## 第三方与协议

YAML 处理复用 YamlDotNet 16.3.0（MIT）；.NET 运行时许可证随本地发布目录附带。见 [第三方声明](THIRD_PARTY_NOTICES.md)。控制器交互依据 [Mihomo 官方 API](https://wiki.metacubex.one/api/)，不嵌入 Mihomo 或 Verge 可执行代码，不自动下载依赖。
