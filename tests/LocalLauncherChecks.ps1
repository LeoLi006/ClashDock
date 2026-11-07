$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $root 'artifacts\local-launcher-check'
$publish=Join-Path $fixture 'publish'
New-Item -ItemType Directory -Path $publish -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'ClashDock.exe') -Destination $fixture
Copy-Item -LiteralPath (Join-Path $root 'publish\ClashDock.exe') -Destination $publish
@{UseManualController=$true;Host='127.0.0.1';Port=1;Secret='';Routing=@{Proxy='';Applications=@();Websites=@()}} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $publish 'settings.json') -Encoding utf8
$entry=Join-Path $fixture 'ClashDock.exe'
$actual=Join-Path $publish 'ClashDock.exe'
$process=$null
try {
 $launcher=Start-Process -FilePath $entry -PassThru
 if(-not $launcher.WaitForExit(5000)){throw 'Launcher did not return'}
 for($i=0;$i -lt 60;$i++){
  $process=Get-Process ClashDock -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $actual} | Select-Object -First 1
  if($process -and $process.MainWindowHandle -ne 0){break}
  Start-Sleep -Milliseconds 100
 }
 if(-not $process -or $process.MainWindowHandle -eq 0){throw 'Double-click entry did not show main window'}
 $second=Start-Process -FilePath $entry -PassThru
 if(-not $second.WaitForExit(5000)){throw 'Second launcher did not exit'}
 Start-Sleep -Milliseconds 500
 $matching=@(Get-Process ClashDock | Where-Object {$_.Path -eq $actual})
 if($matching.Count -ne 1){throw 'Launcher created duplicate app'}
 'PASS: root EXE shows real application, repeated launch keeps one instance; isolated configuration only.'
} finally {if($process -and -not $process.HasExited){Stop-Process -Id $process.Id -Force}}
