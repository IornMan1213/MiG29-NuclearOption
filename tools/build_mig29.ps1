# Headless build of the MiG-29 Blueprinter mod.
# Must be launched via PowerShell Start-Process (launching Unity.exe from Git Bash fails the licensing signature check).
# Unity Hub must be running and signed in.
param(
    [string]$Project = $env:BLUEPRINTER_PROJECT,
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe",
    [switch]$Release,          # back the build up to builds/ and the GitHub `builds` branch (tools/backup_build.py)
    [string]$Note = ""         # notes column for that version
)
$ErrorActionPreference = "Stop"
if (-not $Project -or -not (Test-Path (Join-Path $Project "Assets"))) { throw "Pass -Project <Blueprinter-Editor folder> or set BLUEPRINTER_PROJECT" }
$sa = Join-Path $Project "Library\ScriptAssemblies"
$dlls = "Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll"

function Invoke-Unity([string[]]$extra, [string]$log) {
    $args = @('-batchmode', '-nographics', '-projectPath', "`"$Project`"", '-logFile', "`"$log`"") + $extra
    $p = Start-Process -FilePath $Unity -ArgumentList $args -PassThru -Wait
    return $p.ExitCode
}

# 0a. Sync the editor scripts from this repo into the Unity project (the repo is the source of truth).
$dst = Join-Path $Project "Assets\Editor\MiG29Tools"
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item (Join-Path (Split-Path $PSScriptRoot) "unity\MiG29Tools\*.cs") $dst -Force

# 0b. Generated sources: missile/launcher models, the flight-model export and the livery textures.
$tools = $PSScriptRoot
$src = "$Project\MiG29Source"
Push-Location (Split-Path $tools)
python "$tools\missile_gen.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "missile_gen failed" }
python "$tools\fm_export.py" "$src\fm_unity.json"; if ($LASTEXITCODE -ne 0) { throw "fm_export failed" }
python "$tools\livery_desert.py" "$src\mig29_basecolor.png" "$src\mig29_mesh.json" "$src\mig29_basecolor_desert.png"; if ($LASTEXITCODE -ne 0) { throw "livery_desert failed" }
python "$tools\livery_digital.py" "$src\mig29_basecolor.png" "$src\mig29_mesh.json" "$src\mig29_basecolor_digital.png"; if ($LASTEXITCODE -ne 0) { throw "livery_digital failed" }
python "$tools\livery_display.py" "$src\mig29_basecolor.png" "$src\mig29_mesh.json" "$src\mig29_basecolor_display.png"; if ($LASTEXITCODE -ne 0) { throw "livery_display failed" }
Pop-Location

# 1. Compile pass. Blueprinter keeps the game's Assembly-CSharp in ScriptAssemblies read-only, which makes
#    batchmode compilation fail; clear it so the project compiles.
foreach ($f in $dlls) { $d = Join-Path $sa $f; if (Test-Path $d) { Set-ItemProperty $d IsReadOnly $false } }
$code = Invoke-Unity @('-quit') (Join-Path $Project "MiG29Out_compile.log")
if ($code -ne 0) { throw "compile pass failed ($code), see MiG29Out_compile.log" }

# 2. Re-do Blueprinter's GameAssemblySync (its delayCall never runs in batchmode): game DLLs back, read-only.
foreach ($f in $dlls) {
    $d = Join-Path $sa $f
    if (Test-Path $d) { Set-ItemProperty $d IsReadOnly $false }
    Copy-Item (Join-Path $Project "Packages\nuclearoption\$f") $d -Force
    Set-ItemProperty $d IsReadOnly $true
}

# 3. Build pass.
$env:MIG29_OUT = Join-Path $Project "MiG29Build"
Remove-Item "$env:MIG29_OUT\*.nobp" -ErrorAction SilentlyContinue
$log = Join-Path $Project "MiG29Out_build.log"
$code = Invoke-Unity @('-executeMethod', 'MiG29Tools.MiG29Builder.BuildAll') $log
Select-String -Path $log -Pattern '\[MiG29\]|\[Blueprinter\]|error CS|Exception' | Where-Object { $_.Line -notmatch 'Licensing' } | ForEach-Object { $_.Line }
Get-ChildItem $env:MIG29_OUT -Filter *.nobp | ForEach-Object { "OUTPUT $($_.FullName) $($_.Length)" }
if ($Release -and $code -eq 0) {
    Get-ChildItem $env:MIG29_OUT -Filter *.nobp | ForEach-Object { python (Join-Path $PSScriptRoot "backup_build.py") $_.FullName --note $Note }
}
exit $code
