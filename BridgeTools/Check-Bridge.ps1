#requires -Version 5.1
[CmdletBinding()]
param([string]$InstallDirectory, [switch]$Utf8Output, [switch]$AsJson)
if ($Utf8Output) { [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false) }
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) { $InstallDirectory = [System.IO.Path]::GetDirectoryName($PSScriptRoot) }
$ErrorActionPreference = 'Stop'
$install = (Resolve-Path -LiteralPath $InstallDirectory).Path
$file = Join-Path $install 'native-host\com.clashdock.bridge.json'
$key = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.clashdock.bridge'
$entry = if (Test-Path -LiteralPath $key) { (Get-Item -LiteralPath $key).GetValue('') } else { $null }
$manifest = $null
try { if (Test-Path -LiteralPath $file) { $manifest = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json } } catch { }
$extensionFile = Join-Path $install 'EdgeExtension\manifest.json'
$extensionId = ''
try {
 $extension = Get-Content -LiteralPath $extensionFile -Raw -Encoding UTF8 | ConvertFrom-Json
 $sha = [System.Security.Cryptography.SHA256]::Create()
 try { $hash = $sha.ComputeHash([Convert]::FromBase64String($extension.key)) } finally { $sha.Dispose() }
 $extensionId = -join ($hash[0..15] | ForEach-Object { [char](97 + ($_ -shr 4)); [char](97 + ($_ -band 15)) })
} catch { }

$checks = [pscustomobject]@{
 HostManifestPresent=(Test-Path -LiteralPath $file -PathType Leaf)
 HostManifestValid=($null -ne $manifest)
 ExtensionManifestPresent=(Test-Path -LiteralPath $extensionFile -PathType Leaf)
 ExtensionIdMatches=($extensionId -eq 'opbdfefdnighegepeiffmioofdkkphnk')
 HostNameMatches=($null -ne $manifest -and $manifest.name -eq 'com.clashdock.bridge')
 StdioTransport=($null -ne $manifest -and $manifest.type -eq 'stdio')
 DesktopPresent=(Test-Path -LiteralPath (Join-Path $install 'ClashDock.exe'))
 BridgePresent=(Test-Path -LiteralPath (Join-Path $install 'ClashDock.Bridge.exe'))
 CurrentUserRegistered=($entry -eq $file)
 ExpectedOrigin=($null -ne $manifest -and @($manifest.allowed_origins).Count -eq 1 -and $manifest.allowed_origins[0] -eq 'chrome-extension://opbdfefdnighegepeiffmioofdkkphnk/')
 HostPathMatches=($null -ne $manifest -and $manifest.path -eq (Join-Path $install 'ClashDock.Bridge.exe'))
}
if ($AsJson) { $checks | ConvertTo-Json -Compress; return }
$checks
Write-Output '这是只读安装检查，不验证真实浏览器握手或修改浏览器配置。'
