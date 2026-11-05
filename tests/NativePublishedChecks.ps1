param([string]$DesktopBuildDirectory = "artifacts\ui-build", [string]$BridgeBuildDirectory = "artifacts\bridge-build", [string]$FixtureName = "native-published-fixture")
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path (Join-Path $root 'artifacts') $FixtureName
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
Copy-Item -LiteralPath (Join-Path (Join-Path $root $DesktopBuildDirectory) 'ClashDock.exe') -Destination $fixture -Force
Copy-Item -LiteralPath (Join-Path (Join-Path $root $BridgeBuildDirectory) 'ClashDock.Bridge.exe') -Destination $fixture -Force
$config=Join-Path $fixture 'settings.json'
@{UseManualController=$true;Host='127.0.0.1';Port=1;Secret='';Routing=@{VergeDirectory=(Join-Path $fixture 'no-verge');Proxy='';Applications=@();Websites=@()}} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $config -Encoding utf8
$before=(Get-FileHash -LiteralPath $config).Hash
$info=[System.Diagnostics.ProcessStartInfo]::new((Join-Path $fixture 'ClashDock.Bridge.exe'),'chrome-extension://opbdfefdnighegepeiffmioofdkkphnk/')
$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
$bridge=[System.Diagnostics.Process]::Start($info)
$caller=[Guid]::NewGuid().ToString('N')
function Request($op,$session='') {
 $message=@{version=1;id=[Guid]::NewGuid().ToString('N');client=$caller;op=$op;domain='example.com';mode='自动';session=$session;expiresAt=[DateTimeOffset]::UtcNow.AddSeconds(14).ToUnixTimeMilliseconds()}
 $bytes=[Text.Encoding]::UTF8.GetBytes(($message|ConvertTo-Json -Compress));$stream=$bridge.StandardInput.BaseStream
 $stream.Write([BitConverter]::GetBytes([int]$bytes.Length),0,4);$stream.Write($bytes,0,$bytes.Length);$stream.Flush()
 $reader=[IO.BinaryReader]::new($bridge.StandardOutput.BaseStream,[Text.Encoding]::UTF8,$true)
 $length=$reader.ReadInt32();if($length -lt 2 -or $length -gt 65536){throw 'Invalid reply size'}
 return ([Text.Encoding]::UTF8.GetString($reader.ReadBytes($length))|ConvertFrom-Json)
}
$result=[ordered]@{}
$desktop=Join-Path $fixture 'ClashDock.exe'
try {
 $off=Request 'state';$result.offline_is_not_success=(-not $off.ok -and $off.code -eq 'desktop_offline')
 $started=Request 'start';$result.fixed_start_accepted=($started.ok -and $started.code -eq 'starting')
 Start-Sleep -Milliseconds 1600
 $p=Get-Process ClashDock | Where-Object {$_.Path -eq $desktop} | Select-Object -First 1
 if($null -eq $p){throw 'Fixture desktop did not start'}
 $p.Refresh();$result.start_is_silent=($p.MainWindowHandle -eq 0)
 $state=Request 'state';$result.controller_offline_not_green=($state.ok -and $state.data.state -eq 'controller_offline')
 $check=Request 'check' $state.data.session
 Start-Sleep -Milliseconds 400
 $p.Refresh();$result.check_acknowledged=($check.ok -and $check.code -eq 'opened')
 $result.silent_first_check_window_visible=($p.MainWindowHandle -ne 0 -and $p.MainWindowTitle -like '*检查分流*')
 $result.read_only_config_preserved=($before -eq (Get-FileHash -LiteralPath $config).Hash -and -not (Test-Path -LiteralPath (Join-Path $fixture 'routing-intent.json')))
} finally {
 $bridge.StandardInput.Close();if(-not $bridge.WaitForExit(3000)){$bridge.Kill()}
 Get-Process ClashDock -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $desktop} | Stop-Process -Force
}
$result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'artifacts\native-published-checks.json') -Encoding utf8
$result|ConvertTo-Json
if($result.Values -contains $false){throw 'Published bridge check failed'}
