# Edge 扩展安装与使用（ClashDock 1.2.0）

## 包含什么

- `ClashDock.exe`：桌面端和唯一规则队列。
- `ClashDock.Bridge.exe`：受限标准输入/输出 Native Messaging 通信桥，不持有 Mihomo 密钥。
- `EdgeExtension/`：可加载的 Manifest V3 扩展，固定 ID `opbdfefdnighegepeiffmioofdkkphnk`。
- `BridgeTools/`：注册、只读检查、卸载当前用户通信桥的 PowerShell 脚本，兼容 Windows PowerShell 5.1 / PowerShell 7。

解压到长期保留、当前用户可写的目录，两个 EXE 必须相邻。不要从 ZIP 内直接运行。移动整个目录后需从旧目录卸载注册，再从新目录注册。

## 安装

1. 打开解压目录中的 PowerShell，运行：

   ```powershell
   .\BridgeTools\Register-Bridge.ps1
   ```

   开发目录中可指定发布位置：`-InstallDirectory .\publish`。脚本只写安装目录下 `native-host/com.clashdock.bridge.json`，以及当前用户 `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.clashdock.bridge` 默认值指向该清单；不改浏览器安全策略、不装第三方扩展、不需要常规管理员权限。支持 `-WhatIf` 预览。遇到单位策略限制时遵循本机策略，不自动绕过。

2. 由你打开 `edge://extensions`，开启开发者模式，点“加载解压缩的扩展”，选择包内 `EdgeExtension`。核对 ID 为上述固定 ID，再固定到工具栏。**脚本不会替你执行这一步。**
3. 打开一个 HTTP/HTTPS 网站，点击 ClashDock 图标。初次可先只读观察状态，不必修改规则。
4. 桌面未运行时点击“启动 ClashDock”，启动固定相邻 EXE 的 `--silent` 模式，再刷新状态。若通信桥本身未注册，浏览器不能调用启动入口，需先完成第 1 步。

扩展只申请 `activeTab`、`nativeMessaging`。不申请 proxy、history、cookies、网页内容脚本或全站访问；只在打开弹窗时读取当前活动标签页 URL 来提取域名，不发送 URL 路径、网页密码或表单内容。

## 使用边界

- 当前域名及其子域名生效，同域名标签页共用设置；也可能影响其他应用访问这些域名。
- 不把当前子域自动扩大到主域；内部页面、文件页面、IP/无有效域名地址禁用规则操作。页面加载失败但地址仍是有效网站时，可以操作。
- 自动/直连/代理共用桌面规则、校验和防抖串行队列，不维护第二套配置。显示“已接收”不代表已经生效，最终以状态核验为准。
- 固定跟随全局目标，不提供每网站节点，不接管 Edge 的全局代理；应用流量需先进入 Clash。
- “自动”不一定排除父域名规则，弹窗会提示继承影响。
- 模板先预览和勾选域名，再确认应用；共享 CDN/资源域名可能影响其他网站。
- 默认不刷新，手动“刷新网页”可能丢弃表单，请自行决定；“检查分流”只打开桌面只读面板。
- 桌面暂停时网站修改只保留恢复意图，不能当作已经生效。掉线、失败、超时不显示成功；先刷新状态再决定是否重试。
- 关闭弹窗取消未处理的通信；已被桌面接收并持久化的用户修改不会因关闭而撤销。重连只读同步，不盲目重放写操作。
- 不管理 TUN/系统代理，不保证直连能解决校园网认证，不猜学校地址或保存校园网密码。

## 检查与卸载

```powershell
.\BridgeTools\Check-Bridge.ps1
.\BridgeTools\Unregister-Bridge.ps1
```

只读检查会列出 EXE、注册路径和获准来源是否匹配；不代表实际 Edge 握手一定成功。卸载仅移除属于当前安装的注册项及生成清单，个人规则、配置和 EXE 不删；之后在 Edge 扩展页手动移除扩展。若注册属于其他目录，脚本拒绝覆盖/删除。

## 常见问题

- 扩展连接错误会显示 Edge 返回的原始错误（不再一概称为未注册）。更新弹窗脚本后，在 `edge://extensions` 重新加载扩展；关闭旧弹窗后再打开。
- 注册后旧浏览器仍报告 `Specified native messaging host not found.`：先保存网页工作，完全退出 Edge（包括后台进程）并重新打开，再检查；不要仅凭该提示重复修改安全策略。
- 未找到 Native Messaging host：检查注册是否指向当前目录，两个 EXE 是否存在，扩展 ID 是否正确，是否受单位策略阻止。
- 未连接桌面：先启动；按钮不会使用传入路径启动任意程序。
- 控制器未连接：先处理 Verge，本机密钥仅由桌面读取，不能填进扩展。
- 配置被覆盖：到桌面检查 Verge 后手动重试；不会与 Verge 无限争抢。
- 软件/目录更新后失联：不要只替换扩展公钥；保留固定 ID，重新检查通信桥路径。

## PowerShell 编码兼容

注册、检查和卸载脚本均显式以 UTF-8 读取 JSON，脚本自身保留 UTF-8 BOM，以兼容 Windows PowerShell 5.1 和 PowerShell 7。不要用系统 ANSI 编码另存扩展清单，也不要修改公钥来解决乱码。默认安装目录在脚本正文计算，避免 5.1 参数默认值作用域导致目录为空。卸载先验证清单归属，再删除注册项。

## 本轮验证范围

已验证真实通信桥进程和隔离桌面管道的帧协议、来源拒绝、重复请求和断开重连；弹窗在独立 Chromium 测试目录进行实际渲染及模拟交互。2026-11-05：已在本机执行当前用户注册；PowerShell 5.1 与 7 安装检查全部通过，卸载仅执行 WhatIf。实际发布桥与运行中的桌面端只读 state 通信成功，控制器已连接；返回的待重试状态未被自动重放。直接桥探测不等同于真实 Edge 握手，后者需点击扩展弹窗确认。本次没有修改真实分流规则、系统代理或 TUN，也未重建 EXE。

补充实测：独立临时配置中的真实 Microsoft Edge 加载固定 ID 扩展，通过 Native Messaging 成功收到运行中桌面端的真实 state 回复，控制器已连接，原有待重试意图未执行。用户原有 Edge 会话仍报告 `Specified native messaging host not found.`，尚待保存网页工作后完全退出并重开 Edge 复核；不强制结束用户浏览器。
