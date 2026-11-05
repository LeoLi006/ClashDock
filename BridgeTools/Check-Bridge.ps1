#requires -Version 5.1
[CmdletBinding()]
param([string]$InstallDirectory)
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) { $InstallDirectory = [System.IO.Path]::GetDirectoryName($PSScriptRoot) }
$ErrorActionPreference = 'Stop'
$install = (Resolve-Path -LiteralPath $InstallDirectory).Path
$file = Join-Path $install 'native-host\com.clashdock.bridge.json'
$key = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.clashdock.bridge'
$entry = if (Test-Path -LiteralPath $key) { (Get-Item -LiteralPath $key).GetValue('') } else { $null }
$manifest = if (Test-Path -LiteralPath $file) { Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
[pscustomobject]@{
 HostNameMatches=($null -ne $manifest -and $manifest.name -eq 'com.clashdock.bridge')
 StdioTransport=($null -ne $manifest -and $manifest.type -eq 'stdio')
 DesktopPresent=(Test-Path -LiteralPath (Join-Path $install 'ClashDock.exe'))
 BridgePresent=(Test-Path -LiteralPath (Join-Path $install 'ClashDock.Bridge.exe'))
 CurrentUserRegistered=($entry -eq $file)
 ExpectedOrigin=($null -ne $manifest -and @($manifest.allowed_origins).Count -eq 1 -and $manifest.allowed_origins[0] -eq 'chrome-extension://opbdfefdnighegepeiffmioofdkkphnk/')
 HostPathMatches=($null -ne $manifest -and $manifest.path -eq (Join-Path $install 'ClashDock.Bridge.exe'))
}
Write-Output '这是只读安装检查，不验证真实浏览器握手或修改浏览器配置。'
