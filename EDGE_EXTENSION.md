# Edge 扩展安装与维护

普通使用步骤、截图与常见问题集中在 [使用手册第 6 节](使用说明.md#edge)。本页补充通信桥注册、脚本和技术边界。下载安装包请到 [v1.2.0 发布页](https://github.com/LeoLi006/ClashDock/releases/tag/v1.2.0)。

## 最简单的安装方法

1. 完整解压便携包，确认两个 EXE、`EdgeExtension` 和 `BridgeTools` 相邻。
2. 打开桌面“设置与说明 → Edge 通信桥检查／注册…”。先检查，再点击“注册／修复当前安装”并确认。
3. Edge 打开 `edge://extensions`，开启开发者模式 → 加载解压缩的扩展 → 选择 `EdgeExtension` 文件夹。
4. 固定扩展到工具栏，在普通网页点击图标。安装检查与真实浏览器请求是不同层级，需分别确认。

扩展 ID 必须为 **`opbdfefdnighegepeiffmioofdkkphnk`**。不要修改 manifest 公钥来解决问题，不需要修改浏览器安全策略或管理员级注册表。

## 脚本方式（可选）

普通用户不必执行命令。维护人员可在**解压后的主目录**使用 PowerShell：

```powershell
.\BridgeTools\Check-Bridge.ps1
.\BridgeTools\Register-Bridge.ps1
.\BridgeTools\Check-Bridge.ps1
```

脚本默认使用其父级安装目录；开发目录可显式指定 `-InstallDirectory .\publish`。注册支持 `-WhatIf` 预览。Windows PowerShell 5.1 和 PowerShell 7 均显式以 UTF-8 读取 JSON，脚本保留 BOM，中文目录不依赖系统 ANSI 编码。

注册只生成本安装目录的 `native-host/com.clashdock.bridge.json`，并写当前用户 `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.clashdock.bridge` 的默认值。已有注册属于其他目录时拒绝覆盖；请先从旧目录卸载。不会自动加载浏览器扩展。

## 检查结果怎么读

| 层级 | 通过代表什么 | 不代表什么 |
|---|---|---|
| 文件 | 桌面 EXE、桥 EXE、扩展清单存在 | EXE 已实际完成握手 |
| 清单 | JSON 可解析、名称、stdio 与当前路径匹配 | 浏览器已调用该宿主 |
| HKCU 注册 | 当前用户指向本安装的清单 | 控制器或网络已生效 |
| 固定来源与公钥 ID | 仅允许本项目固定扩展 | 获得任意网页访问权限 |
| 真实 Edge 请求记录 | 本次桌面会话观察到浏览器桥请求，附时间 | 当前连接持续正常或公网出口正确 |

直接从命令行启动桥的探测不会记作浏览器握手。关闭弹窗或重启桌面后，应重新打开扩展验证；历史成功不能替代当前检查。无法识别浏览器父进程链时保守地不记录成功。

## 根据原始错误处理

- **Specified native messaging host not found**：先查文件、清单、HKCU 与固定 ID；不是一律要求重启浏览器。注册正确且有旧会话缓存证据时，保存网页工作后再考虑完整重启。
- **Access ... forbidden**：核对获准扩展来源和设备策略；不修改安全策略绕过。
- **Native host has exited / failed to start**：检查桥 EXE 完整性、执行权限和是否与桌面 EXE 相邻。
- **timeout**：本次结果未确认，先只读刷新，不重复提交未知结果的修改。
- **ClashDock 未运行**：桥已正常注册时可用“启动 ClashDock”打开固定相邻主程序，启动后刷新；桥缺失时先修复安装。
- **控制器未连接**：到桌面处理 Verge 连接；密钥只保留本机，不填到扩展或 Issue。

不凭“有后台进程”强制关闭用户 Edge；不要清理浏览数据或删除个人配置来碰运气。

## 更新与卸载

更新同目录文件后，在 `edge://extensions` 对 ClashDock 点“重新加载”，关闭旧弹窗再打开。移动整个目录前，从旧安装目录执行卸载，再在新目录注册。

```powershell
.\BridgeTools\Unregister-Bridge.ps1
```

卸载先核对清单归属，只移除当前安装对应 HKCU 项及生成清单，不删除个人配置或 EXE。然后在 Edge 扩展页移除扩展。**通信桥卸载不等于撤销已经安装的网络规则**；要撤销规则，请先在桌面“设置与说明 → 移除全部托管规则”。

## 权限与功能边界

- 只申请 `activeTab`、`nativeMessaging`，不申请 proxy、cookies、history、内容脚本或全站访问权限。
- 打开弹窗时读取当前标签页地址用于提取域名，不发送路径、网页正文、表单或密码。
- 同域名及其子域名使用同一规则，也影响其他应用访问同域名；不是标签页独立代理。
- 扩展与桌面共用配置、校验及自动应用队列，跟随全局代理目标，不提供每网站独立节点。
- “重新应用全部分流”先预览数量及影响范围，确认时校验会话和配置版本，取消或过期不执行。
- 刷新网页由用户点击，观察连接由用户主动开始；不管理 TUN、系统代理或校园网认证，不猜学校地址。

验证证据和未验证部分见 [验证记录](VALIDATION.md)，协议见 [开发说明](DEVELOPMENT.md)。
