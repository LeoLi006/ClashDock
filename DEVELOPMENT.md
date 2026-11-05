# ClashDock 1.2.0 实现与开发

## 复用与边界

本轮沿用 `ApplicationCatalog`、`RoutingCompiler`、`RoutingService` 和 `AutoRouting`，没有另造扩展规则库。`AutoRouting` 在 UI 线程持有意图版本和串行 worker，保留现有失败回滚、原子保存、暂停、撤销、重试、普通/静默启动和单实例。`Revision` 用于入口同步，不把文字延迟当作节点标识。

- `MihomoClient.Targets / TargetCache`：一个客户端一份缓存。优先内核历史，记录路径、独立末端身份、时间与状态；快照有上限，异步旧读结果不得覆盖较新测速。全局测量信号量限制并发为 2，取消不写新结果。测组使用独立末端身份，不解析显示字符串，不选择策略组。组路径不明则不推测延迟。
- `ModernSelect.DisplayText`：仅显示格式化，`Items/SelectedItem` 仍是原始名称，刷新 `SetItems` 不发选择事件。
- `PinStore`：规范化完整路径，独立 `ui-preferences.json`，最多 1024 项；不保存整份网络配置，不调用队列。排序和搜索只影响界面。
- `RoutingInspector`：只读核验意图、已提交配置、控制器、模式、托管规则和目标。外部覆盖与待提交修改分开。
- `ConnectionObserver`：用户启动的 15 秒观察，约 250 ms 采样，按结构化身份去重，最多 128 条；输入快照上限 8 MiB / 4096 条，关闭清空。完整路径优先，缺路径才名称候选；域名按边界。轮询不能保证抓到每个极短连接。
- `WebsiteTemplates`：桌面和扩展共用同一份模板；资源域名需显式确认。

## 扩展与本机桥

`EdgeExtension` 是 MV3 弹窗，无内容脚本、无后台全站采集，仅 activeTab/nativeMessaging。`ClashDock.Bridge` 是独立 .NET 控制台 EXE，使用官方 Native Messaging 长度前缀 UTF-8 JSON；共享 `BridgeProtocol.cs`，消息最大 64 KiB、深度 12，未知字段/操作拒绝。读取队列最多 16，断开取消在途转发；stdout 只写协议帧。

桥仅接受固定 `chrome-extension://opbdfefdnighegepeiffmioofdkkphnk/`。注册清单的 allowed_origins 仅此一项；扩展 manifest 公钥固定 ID。桥只连接同用户、同安装目录派生的命名管道；启动动作只可启动相邻 `ClashDock.exe --silent`，不存在传入命令或任意文件读写接口。不会修改 Edge 全局代理或浏览器安全策略。

`DesktopBridge` 的管道限制当前用户、最多 4 个客户端，读写和请求有期限，检测断开，UI 调度前再次校验时效。`BridgeDispatcher` 使用桌面唯一队列；非状态请求按 session/revision 校验，写操作还需 client/sequence，缓存最多 256 个请求结果（5 分钟）及 128 个客户端序号，清理后不保留浏览正文。重连读状态，不自动重放未知写结果。桌面停止取消并释放 listener/client，丢弃旧回调。

允许操作：`state`、`templates`、`setSite`、`applyTemplate`、`retry`、`check`；`start` 仅由桥处理固定程序。所有网站域名仍经 `RoutingCompiler.Domain` 规范化。模板域名必须来自已知模板的勾选集合；没有每网站独立节点字段。返回仅网站意图、全局目标显示、暂停/模式/核验状态，不返回密钥、完整配置、应用路径或诊断正文。

本地同用户管道/来源白名单并非防御同账户恶意程序的隔离沙箱；本轮未引入代码执行型插件或浏览器策略变更。

## 协议例子（版本 1）

```json
{"version":1,"id":"0123456789abcdef0123456789abcdef","client":"abcdef0123456789abcdef0123456789","op":"state","domain":"video.example.com","expiresAt":1790000000000}
```

真实请求 expiresAt 为当前时间后 14 秒内（上限 30 秒）；客户端本地等待 12 秒，桥转发最多 15 秒。写操作还带最新 `session`、`baseRevision`、递增 `sequence`，以及 `mode`；模板带 `template` 和勾选 `domains`。响应带相同 id/version、ok/code/message/data。`accepted` 仅接收意图，data.state 才是分流状态；控制器未连接、覆盖、重试和未知不得标成生效。

新增操作必须同步更新白名单、严格 DTO、参数校验、到期/重复规则、最小权限说明和隔离测试；不得用协议扩展执行任意程序、读文件或扩大模型/网页权限。

## 构建与相关检查

```powershell
dotnet build tests/ClashDock.Checks.csproj -c Release
dotnet build Bridge/ClashDock.Bridge.csproj -c Release
.\tests\bin\Release\net9.0-windows\win-x64\ClashDock.Checks.exe
node tests/ExtensionChecks.cjs
node tests/RenderPopup.cjs
dotnet publish ClashDock.csproj -c Release -o artifacts/ui-build
dotnet publish Bridge/ClashDock.Bridge.csproj -c Release -o artifacts/bridge-build
.\tests\InstanceChecks.ps1
.\tests\NativePublishedChecks.ps1
```

真实桥集成测试需先构建 Bridge；`RenderPopup` 使用独立 Chrome 测试目录和模拟 API，不安装扩展，不动个人浏览器配置。无 Node 运行依赖，Node 仅用于开发检查。桥没有新增 NuGet 包，离线恢复可使用本地 `.packages`；离线运行不等于完成在线漏洞审计。

`--preview-ui`、`--discovery-check` 使用隔离数据；`--live-check` 只读控制器数量和模式，不测速、不提交规则。具体证据见 [验证结果](VALIDATION.md)。安装 EXE 时保留 settings、意图和界面偏好；干净分享包不包含它们或本机注册清单。


## 状态可靠性增量（2026-11-05）

`AutoRouting` 以显式 phase、FailureCode、FailureMessage 和 IntentSaved 表达意图持久化及执行状态，不再从 Status 中文正文推断。RoutingFailure 为有明确证据的应用前失败携带类型，其他失败保留原原因。RoutingInspector 继续复用编译器核验，额外确认 DIRECT／PROXY／默认组的真实选择；RoutingState 为扩展和检查面板生成同一结论与下一步。运行模式不是 rule 时，有规则的应用在写入前拒绝，不自动切换模式。最近 IPv6 类型与 GEOIP 两字符国家代码规范化保持不变，不扩大模糊匹配。

版本 1 兼容增加 `snapshot`（同会话单调序号）、intentSaved、phase、reason、stateMessage、next、聚合规则数量及 retryScope。phase: unknown/waiting/applying/saved/applied/paused/retry/save_failed/controller_offline；核验另区分 mode_bypass/overwritten/target_missing/target_mismatch。applied 仅表示规则加载，不代表出口或连接已观测。

`retry` 必须附 `confirmAll:true` 和预览时的 session/baseRevision。缺确认返回 confirmation_required；过期或已变化的预览拒绝执行。旧包格式与已存路由不变，旧客户端仍可读取／选择；旧的未确认 retry 不会静默执行。界面仅展示计数与范围，不发送应用路径清册。

扩展以 revision/snapshot、当前域名和已退休 session 拒绝过期数据；冲突不再自动重放写请求。浏览器错误原文用 textContent 显示，权限仍只有 activeTab/nativeMessaging。

BridgeHealth 复用 PowerShell 5.1/7 安装脚本，固定脚本名及 ArgumentList，重定向 UTF8 输出，15 秒上限和关闭取消；不传任意命令。BridgeEnvelope 的 BrowserProcessObserved 来自桥对 msedge／cmd→msedge 的有限父进程检查，不来自扩展参数。桌面只记录只读请求时间，不持久化；这是诊断线索，并非防御同账户恶意软件的密码学证明，也不意味着当前仍连接。直接启动桥不会产生此记录。未识别的启动链保守地不记录成功。

定向检查：测试 EXE 支持 --subscription-only（10项）及 --reliability-only（状态、队列、失败、取消、旧配置、脚本 UTF8、隔离界面）；node tests/ExtensionChecks.cjs 与 tests/RenderPopup.cjs 验证扩展通信、过期响应、全量重试预览及 100/125/150% 等效视口。模拟写入仅使用隔离控制器；真实部署只读核验，不能沿用以前的一次授权去重放真实规则。


## v1.2.0 正式交付

面向使用者的首页是 README，完整操作说明在使用说明.md；docs/使用手册.html 为带内嵌示意图的离线手册。更新 Markdown 后运行 `python tools/build_user_guide.py` 生成，开发环境需 Python Markdown（软件使用者不需要 Python）。

正式构建分别输出到 artifacts/release-v1.2.0/build/desktop 与 build/bridge。`tools/package_release.py --desktop <桌面EXE> --bridge <桥EXE>` 只按白名单收集构建结果、公共扩展、维护脚本、说明及许可证，拒绝混入已有安装目录。输出 ZIP、独立 HTML 手册、内外 SHA256SUMS 和本地检查报告。不将整个 publish 目录复制上传。

包内 BUILD_INFO 记录打包时的源码提交，正式版本使用 v1.2.0 标签。发布前使用 `tests/InstanceChecks.ps1 -DesktopBuildDirectory release-v1.2.0\build\desktop -FixtureName release-instance-fixture` 和 `tests/NativePublishedChecks.ps1 -DesktopBuildDirectory artifacts\release-v1.2.0\build\desktop -BridgeBuildDirectory artifacts\release-v1.2.0\build\bridge -FixtureName release-native-fixture` 检查候选二进制。它们只在独立夹具写示例配置，不注册真实桥、不提交真实网络修改。
