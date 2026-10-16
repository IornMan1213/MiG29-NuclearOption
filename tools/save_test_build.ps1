# Saves a copy of the current test build as <release version>.<test number>, e.g. MiG-29_0.9.2.3, in builds\test-builds.
# usage: tools\save_test_build.ps1 mig|smt ["what changed"]
param([Parameter(Mandatory)][ValidateSet("mig", "smt")][string]$Jet, [string]$Note = "")

$out = "C:\Users\jayea\Documents\MiG29-NuclearOption\builds\test-builds"
if ($Jet -eq "mig") {
    $name = "MiG-29"; $repo = "C:\Users\jayea\Documents\MiG29-NuclearOption"
    $build = "H:\Blueprinter-Editor\Blueprinter-Editor\MiG29Build"; $dll = "$repo\unity\MiG29Instruments\bin\Release\MiG29Instruments.dll"
} else {
    $name = "MiG-29SMT"; $repo = "C:\Users\jayea\Documents\MiG29SMT-NuclearOption"
    $build = "H:\Blueprinter-Editor\Blueprinter-Editor\MiG29SMTBuild"; $dll = "$repo\unity\MiG29Instruments\bin\Release\MiG29SMTInstruments.dll"
}

$nobp = Get-ChildItem $build -Filter *.nobp | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $nobp) { throw "no .nobp in $build" }
if ($nobp.BaseName -notmatch '_(\d+\.\d+\.\d+)$') { throw "no version in $($nobp.Name)" }
$ver = $Matches[1]

New-Item -ItemType Directory -Force $out | Out-Null
$n = 0
Get-ChildItem $out -Directory -Filter "${name}_$ver.*" | ForEach-Object {
    if ($_.Name -match "^$([regex]::Escape($name))_$([regex]::Escape($ver))\.(\d+)$") { $n = [Math]::Max($n, [int]$Matches[1]) }
}
$dir = Join-Path $out "${name}_$ver.$($n + 1)"
New-Item -ItemType Directory $dir | Out-Null
Copy-Item $nobp.FullName $dir
if (Test-Path $dll) { Copy-Item $dll $dir }
$commit = git -C $repo log -1 --format="%h %s"
@("$name $ver.$($n + 1)", "nobp built: $($nobp.LastWriteTime)", "commit: $commit", "saved: $(Get-Date)", $Note) | Set-Content -Encoding utf8 (Join-Path $dir "build.txt")
Write-Output "saved $dir"
