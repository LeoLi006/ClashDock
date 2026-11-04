# ClashDock

用于辅助管理本地 Clash Verge Rev / Mihomo 的 Windows 托盘工具。当前为早期版本，存在已知问题。

## 已实现

- 托盘状态显示，每 4 秒刷新连接、模式和 Selector 策略组。
- 切换 rule / global / direct 模式与策略组选项。
- 应用场景预设、保存当前模式和策略组选择为场景。
- 深色控制面板，Ctrl+Alt+Shift+F10 唤起。
- 启动时可自动切回规则模式（仅初次连接成功时）。

## 运行与构建

需要 Windows x64；源码构建需要 .NET 9 SDK。先启动 Clash Verge，并开启 Mihomo 外部控制器。

```powershell
dotnet publish ClashDock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

运行 publish/ClashDock.exe，首次运行会在 exe 目录生成 settings.json。设置 Host、Port、Secret 与外部控制器一致，修改后重新启动工具。

工具通过 REST API 修改内核运行状态，不会编辑 Verge 配置文件；相关状态是否持久化由 Verge / Mihomo 决定。

## 已知限制

- Hotkey 配置目前尚未接入，实际快捷键固定。
- 默认“外网走代理”场景仅切换为 rule 模式，不会添加分流规则或选择节点。
- 面板需手动刷新；操作失败反馈与配置保存提示尚不完善。
- 场景按组名包含匹配，仅操作首个匹配组；缺失目标会跳过。
- 未提供系统代理 / TUN 开关、规则编辑、订阅管理及节点测速。

原有使用说明见 [使用说明.md](使用说明.md)，其中外部分流配置和浏览器扩展步骤不属于程序已实现功能，历史环境描述也不代表当前运行状态。

## 提交范围

仓库只包含源码和文档；构建产物、本地 settings.json 和 Clash 订阅备份不纳入版本管理。
