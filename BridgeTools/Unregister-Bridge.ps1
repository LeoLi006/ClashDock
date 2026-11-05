#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param([string]$InstallDirectory)
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) { $InstallDirectory = [System.IO.Path]::GetDirectoryName($PSScriptRoot) }
$ErrorActionPreference = 'Stop'
$install = (Resolve-Path -LiteralPath $InstallDirectory).Path
$file = Join-Path $install 'native-host\com.clashdock.bridge.json'
$key = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.clashdock.bridge'
if (Test-Path -LiteralPath $key) {
 if ((Get-Item -LiteralPath $key).GetValue('') -ne $file) { throw '注册项属于其他安装目录，未删除。请从对应目录运行卸载。' }
}
if (Test-Path -LiteralPath $file) {
 $manifest = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
 if ($manifest.name -ne 'com.clashdock.bridge' -or $manifest.path -ne (Join-Path $install 'ClashDock.Bridge.exe')) { throw '清单不属于本安装，未删除。' }
}
if (Test-Path -LiteralPath $key) {
 if ($PSCmdlet.ShouldProcess('当前用户 ClashDock Native Messaging 注册项', '删除本工具注册项')) { Remove-Item -LiteralPath $key }
}
if (Test-Path -LiteralPath $file) {
 if ($PSCmdlet.ShouldProcess('本工具 native-host/com.clashdock.bridge.json', '删除生成的通信桥清单')) { Remove-Item -LiteralPath $file }
}
Write-Output '检查完成。个人规则与配置未删除；在 Edge 扩展页手动移除扩展即可。'
