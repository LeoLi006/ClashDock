# ClashDock

**为常用 Windows 应用和网站快速选择：自动、直连或代理。**

独立桌面工具，复用本机 [Clash Verge Rev](https://github.com/clash-verge-rev/clash-verge-rev) / [Mihomo](https://github.com/MetaCubeX/mihomo) 的节点与规则，附可选 Edge 扩展。不附带订阅，不管理 TUN，不强制接管所有软件。可单独运行，也可从 [Leo 桌面助手](https://github.com/LeoLi006/leo-desktop) 等启动入口打开。

## 下载使用

### [下载 v1.2.0 Windows x64 便携包](https://github.com/LeoLi006/ClashDock/releases/download/v1.2.0/ClashDock-1.2.0-win-x64.zip)

[正式发布页与全部附件](https://github.com/LeoLi006/ClashDock/releases/tag/v1.2.0) · [在线使用手册](使用说明.md) · [可下载的离线 HTML 手册](https://github.com/LeoLi006/ClashDock/releases/download/v1.2.0/ClashDock-1.2.0-User-Guide.html)

**普通用户下载 Assets 中的 `ClashDock-1.2.0-win-x64.zip`，不要下载 `Source code`。** 解压后双击 `ClashDock.exe`，无需安装 .NET、Python、Node.js 或开发环境。面向 Windows 10 / 11 x64；两个 EXE 和配套文件夹请放在一起，勿在 ZIP 内直接运行。

## 第一次怎么用

1. **先打开 Verge**：已有可用节点或订阅，且使用支持全局扩展脚本的配置；优先 Clash Verge Rev。
2. **在 Verge 自行确认 Rule 模式**：ClashDock 不替你切换系统代理、TUN 或运行模式。应用流量必须已经进入 Clash。
3. **双击 ClashDock.exe**：普通启动显示工作台，再次双击会唤起已有窗口。
4. **选择应用或网站的模式**：左侧“应用规则”搜索软件，或“网站规则 → 添加网站”；选择代理前在顶部选好目标。
5. **等待“规则已加载”**：修改自动保存并串行应用，无需日常点击“重新应用全部分流”。想看实际命中，点“检查分流 → 开始观察 15 秒”，再手动操作网页／应用。

“直连”按钮表示你的选择；“预期出口”不是公网出口实测；“规则已加载”只代表内核配置核验。全局模式、规则缺失和应用失败都会单独提示，不会把按钮选中当成已经生效。

## 常用入口

| 需求 | 操作位置 |
|---|---|
| 设置应用模式、搜索应用 | 应用规则 |
| 常用应用置顶 | 名称旁 ☆ / ★，或右键置顶 |
| 设置网站及子域名 | 网站规则 → 添加网站 |
| 节点延迟与测速 | 顶部“测速 / 刷新”或“目标状态” |
| 查看实际命中 | 选中条目 → 检查分流 |
| 安装 Edge 通信桥 | 设置与说明 → Edge 通信桥检查／注册 |
| 失败恢复 | 重新应用全部分流 → 看数量和影响范围 → 确认 |
| 暂停／恢复或退出 | Windows 右下角托盘图标右键 |

**退出保留已安装规则，暂停才撤下本工具规则。** 重新应用作用于全部保存的分流，并非只修当前网站；失败保留选择，不无限重试或与 Verge 持续争抢配置。

## Edge 扩展（可选）

1. 桌面“设置与说明 → Edge 通信桥检查／注册”中先检查，再注册当前安装。
2. Edge 打开 `edge://extensions`，开启开发者模式 → 加载解压缩的扩展 → 选择包内 **EdgeExtension** 文件夹。
3. 在普通网页点击工具栏 ClashDock 图标，选择自动／直连／代理。

固定 ID：`opbdfefdnighegepeiffmioofdkkphnk`。同域名标签页共用设置，也可能影响其他应用访问该域名；不是标签页独立代理。节点详情折叠，不自动刷新网页；不读取网页正文、密码、表单或完整浏览历史。

首次安装、逐项操作、更新卸载及常见故障请看 **[使用手册](使用说明.md)**；注册脚本及具体通信错误见 [Edge 安装指南](EDGE_EXTENSION.md)。

## 相关开源项目

| 项目 | 与 ClashDock 的关系 |
|---|---|
| [Clash Verge Rev](https://github.com/clash-verge-rev/clash-verge-rev) | 推荐搭配的代理客户端，负责订阅、节点和内核运行；需要另行安装并配置。 |
| [Mihomo](https://github.com/MetaCubeX/mihomo) | 提供进程／域名分流和控制器 API 的代理内核；通常由 Verge 自带和管理。 |
| [Leo 桌面助手](https://github.com/LeoLi006/leo-desktop) | 可选桌面工具，可将 ClashDock 配置为快捷启动目标；只负责打开窗口，不参与 ClashDock 的规则执行，也不是必需依赖。 |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | ClashDock 使用的 YAML 解析库，随软件提供，无需用户单独安装。许可证见 [第三方声明](THIRD_PARTY_NOTICES.md)。 |

ClashDock 与以上项目分别维护，不打包分发 Clash Verge Rev、Mihomo 或 Leo。日常使用只需保持 Verge 正常运行，再由 ClashDock 管理应用和网站的分流选择。

## 文档导航

- [使用手册](使用说明.md)：从下载到日常操作，含“直连为什么仍显示代理 IP”等常见问题。
- [离线 HTML 手册源码](docs/使用手册.html)：正式包内可直接双击阅读。
- [更新日志](CHANGELOG.md)：v1.2.0 功能与修复。
- [开发说明](DEVELOPMENT.md)：实现、通信协议、构建与测试。
- [验证记录](VALIDATION.md)：严格区分模拟测试、真实只读和真实写入。
- [第三方声明](THIRD_PARTY_NOTICES.md)：依赖与许可证。

## 开发者从源码构建

普通用户使用上方 ZIP 即可。本节仅面向 Windows 上的开发者，需要 .NET 9 SDK。

```powershell
dotnet publish ClashDock.csproj -c Release -o publish
dotnet publish Bridge/ClashDock.Bridge.csproj -c Release -o publish
Copy-Item EdgeExtension, BridgeTools -Destination publish -Recurse -Force
.\publish\ClashDock.exe
```

测试与版本化发布流程见 [开发说明](DEVELOPMENT.md)。源码和 Release 分别维护，发布包对应固定版本标签。个人配置、密钥、意图、撤销记录、浏览器测试目录与本机注册清单不进入 Git 或分享包。

## 使用边界与反馈

本版本不附商业代码签名，Windows 可能提示无法识别发布者；请核对正式来源与 SHA-256，不要关闭系统防护或绕过单位策略。普通运行及当前用户桥注册一般不需管理员权限。

真实规则写入会改变网络，请先理解模式和影响范围。已有连接可能沿用原路线，短连接采样可能漏过。版本验证及未验证部分见 [验证记录](VALIDATION.md)。

[提交问题](https://github.com/LeoLi006/ClashDock/issues) 请附版本和复现步骤，遮住密钥、订阅、私人网站及本机个人路径；不要上传整个安装目录。
