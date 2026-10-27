# Headless build of the MiG-29 Blueprinter mod (Windows PowerShell, or pwsh on Linux).
# On Windows, must be launched via PowerShell Start-Process (launching Unity.exe from Git Bash fails the licensing signature check).
# Unity Hub must be running and signed in.
param(
    [string]$Project = $env:BLUEPRINTER_PROJECT,
    [string]$Unity = $(if ($env:UNITY_EDITOR) { $env:UNITY_EDITOR } elseif ($env:OS -eq 'Windows_NT') { "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe" } else { "$HOME/Unity/Hub/Editor/2022.3.62f2/Editor/Unity" }),
    [string]$Blender = $(if ($env:BLENDER) { $env:BLENDER } elseif ($env:OS -eq 'Windows_NT') { "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" } else { "blender" }),
    [switch]$Release,          # back the build up to builds/ and the GitHub `builds` branch (tools/backup_build.py)
    [string]$Note = "",        # notes column for that version
    [switch]$Quick             # skip the generated sources (models, liveries, cockpit mesh) when they are already there: for
                               # cockpit editor changes, which only the Unity pass uses
)
$ErrorActionPreference = "Stop"
$py = if ($env:OS -eq 'Windows_NT') { "python" } else { "python3" }
if (-not $Project -or -not (Test-Path (Join-Path $Project "Assets"))) { throw "Pass -Project <Blueprinter-Editor folder> or set BLUEPRINTER_PROJECT" }
$sa = Join-Path $Project "Library/ScriptAssemblies"
$dlls = "Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll"

function Invoke-Unity([string[]]$extra, [string]$log) {
    $args = @('-batchmode', '-nographics', '-projectPath', "`"$Project`"", '-logFile', "`"$log`"") + $extra
    $p = Start-Process -FilePath $Unity -ArgumentList $args -PassThru -Wait
    return $p.ExitCode
}

# 0a. Sync the editor scripts from this repo into the Unity project (the repo is the source of truth).
$dst = Join-Path $Project "Assets/Editor/MiG29Tools"
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item (Join-Path (Split-Path $PSScriptRoot) "unity/MiG29Tools/*.cs") $dst -Force

# 0b. Generated sources: missile/launcher models, the flight-model export and the livery textures.
$src = "$Project/MiG29Source"
# cockpit editor elements (decals, custom screens): <repo>/cockpit/build, written by the Cockpit Editor
Remove-Item "$src/custom" -Recurse -Force -ErrorAction SilentlyContinue
$customBuild = Join-Path (Split-Path $PSScriptRoot) "cockpit/build"
if (Test-Path "$customBuild/custom_mesh.json") { New-Item -ItemType Directory -Force "$src/custom" | Out-Null; Copy-Item "$customBuild/*" "$src/custom" -Force }
# -Quick skips Blender only when the cockpit mesh there already has the editor's groups (a build from a branch without them
# writes one without, and the layout could not be applied)
$hasGroups = $false
if ($Quick -and (Test-Path "$src/cockpit_mesh.json")) {
    $hasGroups = (& $py -c "import json,os;p=r'$src';m=json.load(open(os.path.join(p,'cockpit_mesh.json')));b=os.path.join(p,'cockpit_mesh_base.json');m=json.load(open(b)) if m.get('layout_applied') and os.path.exists(b) else m;print(1 if m.get('groups') else 0)") -eq "1"
}
if (-not ($Quick -and $hasGroups)) {
$tools = $PSScriptRoot
Push-Location (Split-Path $tools)
& $py "$tools/fix_winding.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "fix_winding failed" }   # before anything copies body triangles
& $py "$tools/clean_insignia.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "clean_insignia failed" }
& $py "$tools/normal_map_y.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "normal_map_y failed" }   # the skin's normal map in Unity's convention
& $py "$tools/damage_textures.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "damage_textures failed" }   # after fix_winding (mesh) and normal_map_y
& $py "$tools/missile_gen.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "missile_gen failed" }
& $py "$tools/nozzle_gen.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "nozzle_gen failed" }
& $py "$tools/nose_bay.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "nose_bay failed" }
& $py "$tools/gear_wells.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "gear_wells failed" }
& $py "$tools/main_gear_gen.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "main_gear_gen failed" }
& $py "$tools/main_bay_door.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "main_bay_door failed" }
& $py "$tools/airbrake_cut.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "airbrake_cut failed" }
& $py "$tools/fod_door.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "fod_door failed" }
& $py "$tools/main_wells.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "main_wells failed" }
& $py "$tools/bay_texture.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "bay_texture failed" }
& $py "$tools/fm_export.py" "$src/fm_unity.json"; if ($LASTEXITCODE -ne 0) { throw "fm_export failed" }
& $py "$tools/livery_desert.py" "$src/mig29_basecolor.png" "$src/mig29_mesh.json" "$src/mig29_basecolor_desert.png"; if ($LASTEXITCODE -ne 0) { throw "livery_desert failed" }
& $py "$tools/livery_digital.py" "$src/mig29_basecolor.png" "$src/mig29_mesh.json" "$src/mig29_basecolor_digital.png"; if ($LASTEXITCODE -ne 0) { throw "livery_digital failed" }
& $py "$tools/livery_display.py" "$src/mig29_basecolor.png" "$src/mig29_mesh.json" "$src/mig29_basecolor_display.png"; if ($LASTEXITCODE -ne 0) { throw "livery_display failed" }
& $py "$tools/livery_profiles.py" "$src/mig29_basecolor.png" "$src/mig29_mesh.json" "$src"; if ($LASTEXITCODE -ne 0) { throw "livery_profiles failed" }
# hand-painted livery (blender/livery_edit.py) when there is one
$custom = Join-Path (Split-Path $PSScriptRoot) "blender/livery/mig29_livery_custom.png"
Remove-Item "$src/mig29_basecolor_custom.png" -ErrorAction SilentlyContinue
if ((Test-Path $custom) -and (Get-FileHash $custom).Hash -ne (Get-FileHash "$src/mig29_basecolor.png").Hash) {   # not until it has been painted
    Copy-Item $custom "$src/mig29_basecolor_custom.png" -Force
}
# cockpit: texture atlas, then the from-scratch interior modelled in Blender (fitted to mig29_mesh.json)
& $py "$tools/cockpit_atlas.py" "$src"; if ($LASTEXITCODE -ne 0) { throw "cockpit_atlas failed" }
$ckOld = if (Test-Path "$src/cockpit_mesh.json") { (Get-Item "$src/cockpit_mesh.json").LastWriteTime } else { [datetime]::MinValue }
& $Blender -b --python "$(Split-Path $tools)/blender/cockpit_build.py" -- "$src" | Select-String '\[cockpit\]|Error|Traceback' | ForEach-Object { $_.Line }
if (-not (Test-Path "$src/cockpit_mesh.json") -or (Get-Item "$src/cockpit_mesh.json").LastWriteTime -le $ckOld) { throw "cockpit_build failed" }
Pop-Location
}

# cockpit editor layout (moved / turned / scaled / hidden instruments): the builder sets the instrument groups' transforms from
# <source>/custom/layout.json (they are their own objects, which the plugins also move at runtime for .cpit setups). A cockpit
# mesh an older build baked a layout into is put back to Blender's output first.
if ((Test-Path "$src/cockpit_mesh_base.json") -and (Select-String -Path "$src/cockpit_mesh.json" -Pattern '"layout_applied": true' -SimpleMatch -Quiet)) { Copy-Item "$src/cockpit_mesh_base.json" "$src/cockpit_mesh.json" -Force }
$layoutFile = Join-Path (Split-Path $PSScriptRoot) "cockpit/layout.json"
if (Test-Path $layoutFile) { New-Item -ItemType Directory -Force "$src/custom" | Out-Null; Copy-Item $layoutFile "$src/custom/layout.json" -Force }

# 1. Compile pass. Blueprinter keeps the game's Assembly-CSharp in ScriptAssemblies read-only, which makes
#    batchmode compilation fail; clear it so the project compiles.
foreach ($f in $dlls) { $d = Join-Path $sa $f; if (Test-Path $d) { Set-ItemProperty $d IsReadOnly $false } }
$code = Invoke-Unity @('-quit') (Join-Path $Project "MiG29Out_compile.log")
if ($code -ne 0) { throw "compile pass failed ($code), see MiG29Out_compile.log" }

# 2. Re-do Blueprinter's GameAssemblySync (its delayCall never runs in batchmode): game DLLs back, read-only.
foreach ($f in $dlls) {
    $d = Join-Path $sa $f
    if (Test-Path $d) { Set-ItemProperty $d IsReadOnly $false }
    Copy-Item (Join-Path $Project "Packages/nuclearoption/$f") $d -Force
    Set-ItemProperty $d IsReadOnly $true
}

# 3. Build pass.
$env:MIG29_OUT = Join-Path $Project "MiG29Build"
Remove-Item "$env:MIG29_OUT/*.nobp" -ErrorAction SilentlyContinue
$log = Join-Path $Project "MiG29Out_build.log"
$code = Invoke-Unity @('-executeMethod', 'MiG29Tools.MiG29Builder.BuildAll') $log
Select-String -Path $log -Pattern '\[MiG29\]|\[Blueprinter\]|error CS|Exception' | Where-Object { $_.Line -notmatch 'Licensing' } | ForEach-Object { $_.Line }
Get-ChildItem $env:MIG29_OUT -Filter *.nobp | ForEach-Object { "OUTPUT $($_.FullName) $($_.Length)" }
if (-not (Get-ChildItem $env:MIG29_OUT -Filter *.nobp)) {
    if (Select-String -Path $log -SimpleMatch "Windows build support is not installed" -Quiet) {
        throw "No .nobp: Unity's Windows Build Support (Mono) module is missing (the game is a Windows build, also under Proton). Unity Hub > Installs > 2022.3.62f2 > Add modules, or: unityhub --headless install-modules --version 2022.3.62f2 --module windows-mono"
    }
    throw "No .nobp came out of the build pass, see $log"
}

# 4. The instruments plugin (BepInEx, ships next to the .nobp): built against the installed game.
$repo = Split-Path $PSScriptRoot
$plugin = Join-Path $repo "unity/MiG29Instruments"
# one version for the mod and the plugin: mod managers (NOMNOM / NOMM) match the release version against the DLL's
$ver = [regex]::Match((Get-Content (Join-Path $repo "unity/MiG29Tools/MiG29Builder.cs") -Raw), 'Version = "([\d.]+)"').Groups[1].Value
if (-not (Select-String -Path "$plugin/Plugin.cs" -SimpleMatch "ModVersion = `"$ver`"" -Quiet)) { throw "Plugin.cs ModVersion must be $ver (MiG29Builder.Version)" }
dotnet build "$plugin/MiG29Instruments.csproj" -c Release -o "$env:MIG29_OUT" -nologo -v q "-p:Version=$ver" | Select-String "error|Warn" | ForEach-Object { $_.Line }
if ($LASTEXITCODE -ne 0) { throw "instruments plugin build failed" }
Get-ChildItem $env:MIG29_OUT -Filter MiG29Instruments.dll | ForEach-Object { "OUTPUT $($_.FullName) $($_.Length)" }

# 5. One archive with everything (the first release asset; what NOMNOM / NOMM install): .nobp + plugin + credits.
$zip = Join-Path $env:MIG29_OUT "MiG-29-Fulcrum_$ver.zip"
Remove-Item "$env:MIG29_OUT/MiG-29-Fulcrum_*.zip" -ErrorAction SilentlyContinue
Copy-Item (Join-Path $repo "mod/CREDITS.txt") $env:MIG29_OUT -Force
Compress-Archive -Path "$env:MIG29_OUT/MiG-29 Fulcrum_$ver.nobp", "$env:MIG29_OUT/MiG29Instruments.dll", "$env:MIG29_OUT/CREDITS.txt" -DestinationPath $zip -Force
"OUTPUT $zip $((Get-Item $zip).Length)"
if ($Release -and $code -eq 0) {
    Get-ChildItem $env:MIG29_OUT -Filter *.nobp | ForEach-Object { & $py (Join-Path $PSScriptRoot "backup_build.py") $_.FullName --note $Note }
}
exit $code
