# Run one editor method headless in the Blueprinter project (see build_mig29.ps1 for why the DLL dance is needed).
param(
    [Parameter(Mandatory = $true)][string]$Method,
    [switch]$Graphics,
    [string]$Project = $env:BLUEPRINTER_PROJECT,
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe"
)
$ErrorActionPreference = "Stop"
if (-not $Project -or -not (Test-Path (Join-Path $Project "Assets"))) { throw "Pass -Project <Blueprinter-Editor folder> or set BLUEPRINTER_PROJECT" }
$dst = Join-Path $Project "Assets\Editor\MiG29Tools"; New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item (Join-Path (Split-Path $PSScriptRoot) "unity\MiG29Tools\*.cs") $dst -Force  # repo is the source of truth
$sa = Join-Path $Project "Library\ScriptAssemblies"
$dlls = "Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll"
function Invoke-Unity([string[]]$extra, [string]$log) {
    $a = @('-batchmode', '-projectPath', "`"$Project`"", '-logFile', "`"$log`"") + $extra
    if (-not $Graphics) { $a = @('-nographics') + $a }
    (Start-Process -FilePath $Unity -ArgumentList $a -PassThru -Wait).ExitCode
}
foreach ($f in $dlls) { $d = Join-Path $sa $f; if (Test-Path $d) { Set-ItemProperty $d IsReadOnly $false } }
if ((Invoke-Unity @('-quit') (Join-Path $Project "MiG29Out_compile.log")) -ne 0) { throw "compile pass failed" }
foreach ($f in $dlls) {
    $d = Join-Path $sa $f
    if (Test-Path $d) { Set-ItemProperty $d IsReadOnly $false }
    Copy-Item (Join-Path $Project "Packages\nuclearoption\$f") $d -Force
    Set-ItemProperty $d IsReadOnly $true
}
$log = Join-Path $Project "MiG29Out_run.log"
$code = Invoke-Unity @('-executeMethod', $Method) $log
Select-String -Path $log -Pattern '\[MiG29\]|error CS|Exception' | Where-Object { $_.Line -notmatch 'Licensing' } | ForEach-Object { $_.Line }
exit $code
