#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param([string]$InstallDirectory, [switch]$Utf8Output)
if ($Utf8Output) { [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false) }
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) { $InstallDirectory = [System.IO.Path]::GetDirectoryName($PSScriptRoot) }
$ErrorActionPreference = 'Stop'
$install = (Resolve-Path -LiteralPath $InstallDirectory).Path
$bridge = Join-Path $install 'ClashDock.Bridge.exe'
$desktop = Join-Path $install 'ClashDock.exe'
$extension = Join-Path $install 'EdgeExtension\manifest.json'
foreach ($file in @($bridge,$desktop,$extension)) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw '请指定包含两个 EXE 和 EdgeExtension 的完整发布目录。' } }
$manifest = Get-Content -LiteralPath $extension -Raw -Encoding UTF8 | ConvertFrom-Json
$bytes = [Convert]::FromBase64String($manifest.key)
$sha = [System.Security.Cryptography.SHA256]::Create()
$hash = $sha.ComputeHash($bytes)
$sha.Dispose()
$id = -join ($hash[0..15] | ForEach-Object { [char](97 + ($_ -shr 4)); [char](97 + ($_ -band 15)) })
if ($id -ne 'opbdfefdnighegepeiffmioofdkkphnk') { throw '扩展公钥与获准扩展 ID 不匹配，停止注册。' }
$folder = Join-Path $install 'native-host'
$file = Join-Path $folder 'com.clashdock.bridge.json'
$key = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.clashdock.bridge'
$hadKey = Test-Path -LiteralPath $key
$previous = if ($hadKey) { (Get-Item -LiteralPath $key).GetValue('') } else { $null }
if ($previous -and $previous -ne $file) { throw '已注册其他目录的 ClashDock 通信桥。请先从原目录卸载注册。' }
$oldText = if (Test-Path -LiteralPath $file) { Get-Content -LiteralPath $file -Raw -Encoding UTF8 } else { $null }
if ($PSCmdlet.ShouldProcess('当前用户 Edge NativeMessagingHosts/com.clashdock.bridge', '写入固定扩展通信桥清单与注册项（不安装扩展、不改安全策略）')) {
 try {
  New-Item -ItemType Directory -Path $folder -Force | Out-Null
  @{name='com.clashdock.bridge';description='ClashDock website routing bridge';path=$bridge;type='stdio';allowed_origins=@("chrome-extension://$id/")} | ConvertTo-Json -Depth 4 | ForEach-Object { [System.IO.File]::WriteAllText(($file+'.tmp'), $_, [System.Text.UTF8Encoding]::new($false)) }
  Move-Item -LiteralPath ($file+'.tmp') -Destination $file -Force
  New-Item -Path $key -Force | Out-Null
  Set-Item -LiteralPath $key -Value $file
  Write-Output "当前用户通信桥已注册。扩展 ID：$id。下一步由你在 Edge 加载 EdgeExtension。"
 } catch {
  if ($null -ne $oldText) { [System.IO.File]::WriteAllText($file, $oldText, [System.Text.UTF8Encoding]::new($false)) } elseif (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
  if ($hadKey) { Set-Item -LiteralPath $key -Value $previous } elseif (Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key }
  throw
 }
}
