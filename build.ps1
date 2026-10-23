# Builds, tests and packages OrbisUI into dist\.
#   .\build.ps1             -> public package (no proprietary sounds/icons) + SHA-256 file for the auto-updater
#   .\build.ps1 -Personal   -> also a personal package with your sound and icon packs included (never upload this one)
param([switch]$Personal, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

[xml]$proj = Get-Content src\OrbisUI\OrbisUI.csproj
$ver = $proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "OrbisUI $ver"

if (-not $SkipTests) {
    dotnet test tests\OrbisUI.Tests\OrbisUI.Tests.csproj -c Release
    if ($LASTEXITCODE) { throw 'Tests failed' }
}

$out = Join-Path $PSScriptRoot 'out\OrbisUI'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish src\OrbisUI\OrbisUI.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=none -o $out
if ($LASTEXITCODE) { throw 'Publish failed' }

Copy-Item README.md, CHANGELOG.md, FEATURES.md -Destination $out
Copy-Item third_party\README.txt (Join-Path $out 'THIRD-PARTY.txt')
foreach ($d in 'Games\PS1','Games\PS2','Games\PS3','Games\PS4','Games\PSP','Games\VITA','Firmware','Emulators') {
    New-Item -ItemType Directory -Force (Join-Path $out $d) | Out-Null
}
'Put each system''s games in its folder (PS1, PS2, PS3, PS4, PSP, VITA), or pick another ROM folder in Settings > Library.' |
    Set-Content (Join-Path $out 'Games\README.txt')
'Put BIOS / firmware files you dumped yourself here.' | Set-Content (Join-Path $out 'Firmware\README.txt')
'Put emulators here in folders named PS1, PS2, PS3, PS4, PSP, VITA (or choose their location in Settings > Emulators).' |
    Set-Content (Join-Path $out 'Emulators\README.txt')

New-Item -ItemType Directory -Force dist | Out-Null
$zip = "dist\OrbisUI-$ver-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$out\*" -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -NoNewline
Write-Host "Package: $zip ($hash)"

if ($Personal) {
    $p = Join-Path $PSScriptRoot 'out\OrbisUI-personal'
    # You run the personal build from out\OrbisUI-personal, so keep its settings, art cache, logs and backups across rebuilds.
    $keep = Join-Path $PSScriptRoot 'out\personal-state'
    if (Get-Process OrbisUI -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$p\*" }) { throw 'Close OrbisUI (personal build) before rebuilding it.' }
    if (Test-Path $p) {
        New-Item -ItemType Directory -Force $keep | Out-Null
        foreach ($d in 'Config','Cache','Logs','Backups','Downloads') { if (Test-Path "$p\$d") { Move-Item "$p\$d" $keep -Force } }
        Remove-Item $p -Recurse -Force
    }
    Copy-Item $out $p -Recurse
    $sounds = '..\UI Sounds'; $icons = '..\source code refrences\OrbisPro-main\OrbisPro\Icons'
    New-Item -ItemType Directory -Force "$p\Data\Sounds", "$p\Data\Icons" | Out-Null
    if (Test-Path $sounds) {
        Copy-Item "$sounds\snd_*.wav" "$p\Data\Sounds"
        Get-ChildItem $sounds -Filter '*Startup*.mp3' | Select-Object -First 1 | Copy-Item -Destination "$p\Data\Sounds\startup.mp3"
    }
    if (Test-Path $icons) { Copy-Item "$icons\*.png" "$p\Data\Icons" }
    foreach ($i in '..\store.png', '..\whats new.png', '..\web browser.png') { if (Test-Path $i) { Copy-Item $i "$p\Data\Icons" } }
    if (Test-Path '..\weh broser.png') { Copy-Item '..\weh broser.png' "$p\Data\Icons\web browser.png" }
    # Boot video, live background and home music the user supplied in ..\orbis
    $media = '..\orbis'; New-Item -ItemType Directory -Force "$p\Data\Media" | Out-Null
    foreach ($m in @(@('splash.mp4','boot.mp4'), @('PS4Background.mp4','background.mp4'), @('Ambience.mp3','music.mp3'))) {
        if (Test-Path "$media\$($m[0])") { Copy-Item "$media\$($m[0])" "$p\Data\Media\$($m[1])" }
    }
    $pz = "dist\OrbisUI-$ver-personal-win-x64.zip"
    if (Test-Path $pz) { Remove-Item $pz }
    Compress-Archive -Path "$p\*" -DestinationPath $pz -CompressionLevel Optimal
    Write-Host "Personal package (do not upload): $pz"
    # Restored after zipping so your settings and keys never end up in the package.
    if (Test-Path $keep) { Get-ChildItem $keep | Move-Item -Destination $p -Force; Remove-Item $keep -Recurse -Force }
}
