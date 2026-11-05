#requires -Version 5.1
[CmdletBinding()]
param([string]$InstallDirectory = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$install = (Resolve-Path -LiteralPath $InstallDirectory).Path
$file = Join-Path $install 'native-host\com.clashdock.bridge.json'
$key = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.clashdock.bridge'
$entry = if (Test-Path -LiteralPath $key) { (Get-Item -LiteralPath $key).GetValue('') } else { $null }
$manifest = if (Test-Path -LiteralPath $file) { Get-Content -LiteralPath $file -Raw | ConvertFrom-Json } else { $null }
[pscustomobject]@{
 DesktopPresent=(Test-Path -LiteralPath (Join-Path $install 'ClashDock.exe'))
 BridgePresent=(Test-Path -LiteralPath (Join-Path $install 'ClashDock.Bridge.exe'))
 CurrentUserRegistered=($entry -eq $file)
 ExpectedOrigin=($null -ne $manifest -and @($manifest.allowed_origins).Count -eq 1 -and $manifest.allowed_origins[0] -eq 'chrome-extension://opbdfefdnighegepeiffmioofdkkphnk/')
 HostPathMatches=($null -ne $manifest -and $manifest.path -eq (Join-Path $install 'ClashDock.Bridge.exe'))
}
Write-Output '这是只读安装检查，不验证真实浏览器握手或修改浏览器配置。'
