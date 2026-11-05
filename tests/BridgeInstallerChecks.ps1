#requires -Version 5.1
# Read-only regression: no registry access or process/network changes.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
foreach($name in @('Register-Bridge.ps1','Check-Bridge.ps1','Unregister-Bridge.ps1')) {
 $path=Join-Path $root ('BridgeTools\'+$name)
 $bytes=[IO.File]::ReadAllBytes($path)
 if($bytes[0] -ne 239 -or $bytes[1] -ne 187 -or $bytes[2] -ne 191){throw "$name missing UTF-8 BOM"}
 $tokens=$null;$errors=$null
 $ast=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
 if($errors.Count){throw "$name syntax error"}
 $reads=$ast.FindAll({param($a) $a -is [Management.Automation.Language.CommandAst] -and $a.GetCommandName() -eq 'Get-Content'},$true)
 foreach($read in $reads){if($read.Extent.Text -notmatch '-Encoding\s+UTF8'){throw "$name implicit JSON encoding"}}
 if((Get-FileHash $path).Hash -ne (Get-FileHash (Join-Path $root ('publish\BridgeTools\'+$name))).Hash){throw "$name publish differs"}
}
$m=Get-Content -LiteralPath (Join-Path $root 'publish\EdgeExtension\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$sha=[Security.Cryptography.SHA256]::Create()
try{$hash=$sha.ComputeHash([Convert]::FromBase64String($m.key))}finally{$sha.Dispose()}
$id=-join($hash[0..15]|ForEach-Object{[char](97+($_ -shr 4));[char](97+($_ -band 15))})
if($id -ne 'opbdfefdnighegepeiffmioofdkkphnk'){throw 'Extension identity changed'}
'PASS: script BOM, syntax, explicit UTF8, publish copies and fixed extension identity; PowerShell '+$PSVersionTable.PSVersion
