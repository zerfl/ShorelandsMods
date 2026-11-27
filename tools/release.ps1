<#
.SYNOPSIS
Builds dist\ShorelandsMods-<version>.zip, the package that players unzip into the game folder.

.DESCRIPTION
Checks the formatting (CSharpier), runs the tests, builds the plugin, checks the DLL, stages the plugin with its
bundled libraries and licences, the README, the guide and the nine example mods, writes a reproducible zip and
checks what is in it. Installs nothing, uploads nothing. Needs the .NET SDK and Python 3 (for the guide page).

.PARAMETER GameDir
The folder that holds Shorelands.exe, with BepInEx installed. Default: the SHORELANDS_DIR environment variable.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\release.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Shorelands Playtest"
#>
param([string]$GameDir = $env:SHORELANDS_DIR)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version 2

$examples = @(
    'chat_helper', 'example', 'icon_pack', 'koi_checklist', 'offering_reminder', 'session_stats', 'sound_pack',
    'stream_ticker', 'ui_skin'
)
$libraries = @('Lua', 'Microsoft.Bcl.TimeProvider', 'Microsoft.Bcl.AsyncInterfaces')

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$pluginPath = 'BepInEx/plugins/ShorelandsMods'

function Fail([string]$message) { throw "release: $message" }

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { Fail "dotnet $($args[0]) failed" }
}

function Find-Python {
    foreach ($name in 'python', 'python3', 'py') {
        $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) { return $command.Source }
    }
    Fail 'Python 3 is not on PATH (it builds the guide page)'
}

if (-not $GameDir) { Fail 'the game folder is not set: pass -GameDir or set SHORELANDS_DIR' }
$GameDir = $GameDir.TrimEnd('\', '/')
if (-not (Test-Path (Join-Path $GameDir 'BepInEx/core/BepInEx.dll'))) { Fail "BepInEx is not installed in '$GameDir'" }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$gameProperty = "-p:GameDir=$GameDir"
$python = Find-Python

$version = (& dotnet msbuild (Join-Path $root 'src') -nologo -getProperty:VersionPrefix $gameProperty | Out-String).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { Fail "not a version: '$version'" }
$changelog = Get-Content (Join-Path $root 'CHANGELOG.md')
$heading = $changelog | Where-Object { $_ -match "^## \[$([regex]::Escape($version))\] - ((\d{4})-(\d{2})-(\d{2})|Unreleased)\s*$" } | Select-Object -First 1
if (-not $heading) { Fail "CHANGELOG.md has no entry for $version" }
# A version that is not released yet packs for testing, with the oldest time a zip can hold.
$unreleased = $heading -match 'Unreleased'
if ($unreleased) { $date = New-Object DateTimeOffset 1980, 1, 1, 0, 0, 0, ([TimeSpan]::Zero) }
else {
    $null = $heading -match '(\d{4})-(\d{2})-(\d{2})'
    $date = New-Object DateTimeOffset ([int]$Matches[1]), ([int]$Matches[2]), ([int]$Matches[3]), 0, 0, 0, ([TimeSpan]::Zero)
}

Push-Location $root
try {
    Invoke-Dotnet tool restore | Out-Null
    Invoke-Dotnet csharpier check .
} finally { Pop-Location }

$tests = & dotnet run --project (Join-Path $root 'tests') -c Release -v q $gameProperty
if ($LASTEXITCODE -ne 0) {
    $tests | Write-Host
    Fail 'the tests failed'
}
$tests | Select-Object -Last 1 | Write-Host
Invoke-Dotnet build (Join-Path $root 'src') -c Release -nologo -v q --no-incremental $gameProperty | Out-Null
$out = Join-Path $root 'src/bin/Release'
$dll = Join-Path $out 'ShorelandsMods.dll'
Invoke-Dotnet run --project (Join-Path $root 'tests') --no-build -c Release -- --assembly $dll

# The Lua-CSharp licence in the repository must be the one Lua.dll carries as a resource.
$latin1 = [Text.Encoding]::GetEncoding(28591)
$luaLicence = $latin1.GetString([IO.File]::ReadAllBytes((Join-Path $root 'package/licenses/Lua-CSharp.LICENSE.txt'))).TrimEnd("`n")
if (-not $latin1.GetString([IO.File]::ReadAllBytes((Join-Path $out 'lib/Lua.dll'))).Contains($luaLicence)) {
    Fail "the Lua-CSharp licence differs from Lua.dll's"
}

# The two Microsoft.Bcl packages carry the same licence files; one copy ships.
$nuget = ((& dotnet nuget locals global-packages --list | Out-String).Trim() -replace '^global-packages:\s*', '')
$timeProvider = Join-Path $nuget 'microsoft.bcl.timeprovider/8.0.0'
$asyncInterfaces = Join-Path $nuget 'microsoft.bcl.asyncinterfaces/8.0.0'
foreach ($name in 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') {
    $a = Get-FileHash (Join-Path $timeProvider $name) -Algorithm SHA256
    $b = Get-FileHash (Join-Path $asyncInterfaces $name) -Algorithm SHA256
    if ($a.Hash -ne $b.Hash) { Fail "the two Microsoft.Bcl packages' $name differ: ship both" }
}

# what the package holds
$stage = Join-Path $root 'build/package'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$target = Join-Path $stage $pluginPath
New-Item -ItemType Directory -Force (Join-Path $target 'licenses'), (Join-Path $stage 'mods') | Out-Null
Copy-Item $dll $target
foreach ($library in $libraries) { Copy-Item (Join-Path $out "lib/$library.dll") $target }
Copy-Item (Join-Path $root 'package/THIRD-PARTY-NOTICES.txt') $target
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $target 'LICENSE.txt')
Copy-Item (Join-Path $root 'package/licenses/Lua-CSharp.LICENSE.txt') (Join-Path $target 'licenses')
Copy-Item (Join-Path $timeProvider 'LICENSE.TXT') (Join-Path $target 'licenses/dotnet-runtime.LICENSE.txt')
Copy-Item (Join-Path $timeProvider 'THIRD-PARTY-NOTICES.TXT') (Join-Path $target 'licenses/dotnet-runtime.THIRD-PARTY-NOTICES.txt')
$readme = (Get-Content -Raw (Join-Path $root 'package/README.txt')).Replace('@VERSION@', $version)
[IO.File]::WriteAllText((Join-Path $stage 'ShorelandsMods-README.txt'), $readme, (New-Object Text.UTF8Encoding $false))
& $python -I (Join-Path $root 'guide/build_html.py') (Join-Path $root 'guide/guide.md') (Join-Path $stage 'mods/_guide.html')
if ($LASTEXITCODE -ne 0) { Fail 'the guide page failed to build' }
foreach ($example in $examples) { Copy-Item -Recurse (Join-Path $root "examples/$example") (Join-Path $stage "mods/$example") }

# sorted entries with the changelog's date, so the same sources give the same zip
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "ShorelandsMods-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
$files = @(Get-ChildItem -Recurse -File $stage)
$prefix = (Resolve-Path $stage).Path.TrimEnd('\', '/').Length + 1
$names = [string[]]@($files | ForEach-Object { $_.FullName.Substring($prefix).Replace('\', '/') })
[Array]::Sort($names, [StringComparer]::Ordinal)
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $names) {
        $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $date
        $stream = $entry.Open()
        try {
            $bytes = [IO.File]::ReadAllBytes((Join-Path $stage $name))
            $stream.Write($bytes, 0, $bytes.Length)
        } finally { $stream.Dispose() }
    }
} finally { $archive.Dispose() }
Remove-Item -Recurse -Force $stage

Invoke-Dotnet run --project (Join-Path $root 'tests') --no-build -c Release -- --package $zip
Write-Host "ShorelandsMods $version, $($names.Count) files, $((Get-Item $zip).Length) bytes"
Write-Host "SHA256 $((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())  $zip"
if ($unreleased) { Write-Host "UNRELEASED: a package for testing. Put the release date into CHANGELOG.md before publishing $version." }
