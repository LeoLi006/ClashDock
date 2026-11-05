param([string]$DesktopBuildDirectory = "ui-build", [string]$FixtureName = "instance-fixture")
$ErrorActionPreference = 'Stop'
$artifactRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WindowProbe {
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
"@
$fixture = Join-Path $artifactRoot $FixtureName
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$exe = Join-Path $fixture 'ClashDock.exe'
Copy-Item -LiteralPath (Join-Path (Join-Path $artifactRoot $DesktopBuildDirectory) 'ClashDock.exe') -Destination $exe -Force
@{UseManualController=$true;Host='127.0.0.1';Port=1;Secret='';Routing=@{Proxy='';Applications=@();Websites=@()}} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $fixture 'settings.json') -Encoding utf8
$result = [ordered]@{}
function VisibleHandle($process) {
 for ($i=0;$i -lt 60;$i++) { $process.Refresh(); if ($process.MainWindowHandle -ne 0 -and [WindowProbe]::IsWindowVisible($process.MainWindowHandle)) { return $process.MainWindowHandle }; Start-Sleep -Milliseconds 100 }
 return [IntPtr]::Zero
}
$p = Start-Process -FilePath $exe -PassThru
try {
 $h = VisibleHandle $p
 $result.normal_launch_visible = $h -ne 0
 if ($h -eq 0) { throw 'Normal launch did not show UI' }
 [WindowProbe]::ShowWindow($h,6) | Out-Null
 $second = Start-Process -FilePath $exe -PassThru
 $result.second_exits = $second.WaitForExit(6000)
 Start-Sleep -Milliseconds 400
 $p.Refresh(); $result.minimized_restored = -not [WindowProbe]::IsIconic($p.MainWindowHandle)
 [WindowProbe]::PostMessage($p.MainWindowHandle,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
 Start-Sleep -Milliseconds 400
 $p.Refresh(); $result.close_keeps_tray = -not $p.HasExited -and $p.MainWindowHandle -eq 0
 $third = Start-Process -FilePath $exe -PassThru
 $result.tray_activation_sender_exits = $third.WaitForExit(6000)
 $result.tray_restored = (VisibleHandle $p) -ne 0
} finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; $p.WaitForExit(5000) | Out-Null } }
$p = Start-Process -FilePath $exe -ArgumentList '--silent' -WindowStyle Hidden -PassThru
try {
 Start-Sleep -Seconds 2
 $p.Refresh(); $result.silent_no_window = $p.MainWindowHandle -eq 0 -and -not $p.HasExited
 $second = Start-Process -FilePath $exe -PassThru
 $result.silent_activation_sender_exits = $second.WaitForExit(6000)
 $result.silent_instance_activated = (VisibleHandle $p) -ne 0
} finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; $p.WaitForExit(5000) | Out-Null } }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactRoot 'instance-checks.json') -Encoding utf8
$result | ConvertTo-Json
if ($result.Values -contains $false) { exit 1 }
