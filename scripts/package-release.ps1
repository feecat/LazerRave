[CmdletBinding()]
param(
    [string]$ApplicationDirectory = '',
    [string]$DestinationDirectory = '',
    [string]$NativeManifest = ''
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (!$ApplicationDirectory) { $ApplicationDirectory = Join-Path $repository 'out/app' }
if (!$DestinationDirectory) { $DestinationDirectory = Join-Path $repository 'out/releases' }
if (!$NativeManifest) { $NativeManifest = Join-Path $repository 'out/build/client/obj/Release/net10.0-windows/win-x64/native-dependencies.txt' }
$source = (Resolve-Path -LiteralPath $ApplicationDirectory).Path.TrimEnd('\')
$destination = [IO.Path]::GetFullPath($DestinationDirectory).TrimEnd('\')
if ($destination -eq $source -or $destination.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release archives must be outside the application directory.'
}
[xml]$versionProperties = Get-Content -LiteralPath (Join-Path $repository 'version.props') -Raw
$version = [string]$versionProperties.Project.PropertyGroup.Version
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(beta|rc)\.[1-9][0-9]*)?$') {
    throw 'version.props must contain MAJOR.MINOR.PATCH[-beta.N|-rc.N].'
}
$executable = Join-Path $source 'LazerRave.exe'
$productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion
if (!$productVersion -or $productVersion.Split('+')[0] -ne $version) {
    throw "Executable version does not match version.props ($version). Rebuild the Release client first."
}
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($executable).IsDebug) { throw 'Debug executables cannot be packaged as releases.' }
$active = @(Get-Process -Name 'LazerRave', 'OpenLR2*' -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and [string]::Equals((Split-Path $_.Path -Parent), $source, [StringComparison]::OrdinalIgnoreCase)
})
if ($active.Count) { throw 'Close applications using this runtime before creating a release archive.' }

$files = [Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal)
function Add-ReleaseFile([string]$Path, [string]$Relative) {
    $file = Get-Item -LiteralPath $Path -Force
    if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Release input must be a regular file: $Path"
    }
    if ($Relative -match '(^|[/\\])\.\.([/\\]|$)' -or [IO.Path]::IsPathRooted($Relative)) {
        throw "Invalid release path: $Relative"
    }
    $files.Add($Relative.Replace('\', '/'), $file.FullName)
}
function Add-ReleaseTree([string]$Relative) {
    $directory = Get-Item -LiteralPath (Join-Path $source $Relative) -Force
    if (!$directory.PSIsContainer -or ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Release input must be a regular directory: $Relative"
    }
    foreach ($item in Get-ChildItem -LiteralPath $directory.FullName -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked release input is not allowed: $($item.FullName)" }
        $child = $Relative + '/' + $item.Name
        if ($item.PSIsContainer) { Add-ReleaseTree $child }
        elseif ($item.Name -notmatch '(?i)\.(log|bak|db|db-wal|db-shm|realm|lr2rep)$' -and $item.Name -notlike '*.Zone.Identifier') {
            Add-ReleaseFile $item.FullName $child
        }
    }
}

foreach ($name in @('LazerRave.exe', 'OpenLR2_x64.exe', 'fmod.dll', 'set-window.ps1')) {
    Add-ReleaseFile (Join-Path $source $name) $name
}
$nativeFiles = @(Get-Content -LiteralPath $NativeManifest | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
if (!$nativeFiles.Count) { throw 'Native dependency manifest is empty.' }
foreach ($relative in $nativeFiles) {
    if ($relative -notmatch '^[A-Za-z0-9_.-]+\.dll$') { throw "Unexpected native dependency path: $relative" }
    Add-ReleaseFile (Join-Path $source $relative) $relative
}
foreach ($tree in @('Resources', 'Localization', 'LR2files/Bgm', 'LR2files/Mouse', 'LR2files/Movie', 'LR2files/Sound', 'LR2files/Theme/LR2')) {
    Add-ReleaseTree $tree
}
foreach ($name in @('black.bmp', 'white.bmp', 'foon.bmp', 'muon.wav', 'title.bmp', 'optionstr.csv',
    'keyconfig_def.xml', 'keyconfig_5_def.xml', 'keyconfig_p_def.xml', 'midi_def.xml',
    'sample_5.bme', 'sample_7.bme', 'sample_9.pms', 'sample_10.bme', 'sample_14.bme')) {
    Add-ReleaseFile (Join-Path $source "LR2files/Config/$name") "LR2files/Config/$name"
}
foreach ($name in @('config.xml', 'openlr2-config.xml')) {
    Add-ReleaseFile (Join-Path $repository "res/release/$name") "LR2files/Config/$name"
}
Add-ReleaseFile (Join-Path $repository 'res/release/settings.toml') 'userdata/settings.toml'
Add-ReleaseFile (Join-Path $repository 'LICENSE') 'LICENSE'
Add-ReleaseFile (Join-Path $repository 'src/osu-lazer/LICENCE') 'licenses/osu-lazer-MIT.txt'
foreach ($notice in Get-ChildItem -LiteralPath (Join-Path $repository 'res/licenses') -File) {
    Add-ReleaseFile $notice.FullName ('licenses/' + $notice.Name)
}
foreach ($required in @('Resources/osu.Game.Resources.dll', 'LR2files/Sound/lr2.lr2ss')) {
    if (!$files.ContainsKey($required)) { throw "Required release resource is missing: $required" }
}
[xml]$config = Get-Content -LiteralPath (Join-Path $repository 'res/release/config.xml') -Raw
[xml]$engineConfig = Get-Content -LiteralPath (Join-Path $repository 'res/release/openlr2-config.xml') -Raw
foreach ($skin in @($config.config.skin.ChildNodes) + @($engineConfig.config.skin.ChildNodes)) {
    if ($skin.InnerText -like 'LR2files*' -and !$files.ContainsKey($skin.InnerText.Replace('\', '/'))) {
        throw "Default skin input is missing: $($skin.InnerText)"
    }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$profileBase64 = & python.exe (Join-Path $PSScriptRoot 'create-release-profile.py')
if ($LASTEXITCODE -ne 0) { throw 'Clean OpenLR2 player profile generation failed. Python 3.11 or newer is required.' }
$profileBytes = [Convert]::FromBase64String(($profileBase64 -join ''))
[IO.Directory]::CreateDirectory($destination) | Out-Null
$releaseName = "LazerRave-$version-win-x64"
$zipPath = Join-Path $destination ($releaseName + '.zip')
$temporary = $zipPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
Write-Host "Creating ZIP release: $releaseName ($($files.Count) files)"
try {
    $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($pair in $files.GetEnumerator()) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $pair.Value,
                    "$releaseName/$($pair.Key)", [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
            foreach ($directory in @('BMS', 'Shared', 'LR2files/Database/Score', 'LR2files/Replay', 'LR2files/Ghost', 'LR2files/SkinCustomize')) {
                $archive.CreateEntry("$releaseName/$directory/") | Out-Null
            }
            $profileStream = $archive.CreateEntry("$releaseName/LR2files/Database/Score/Player.db").Open()
            try { $profileStream.Write($profileBytes, 0, $profileBytes.Length) } finally { $profileStream.Dispose() }
            $entry = $archive.CreateEntry("$releaseName/VERSION.txt")
            $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
            try { $writer.WriteLine("LazerRave $version`nPlatform: Windows x64") } finally { $writer.Dispose() }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $zipPath -Force
    $hashStream = [IO.File]::OpenRead($zipPath)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = ([BitConverter]::ToString($sha.ComputeHash($hashStream))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $hashStream.Dispose() }
    [IO.File]::WriteAllText($zipPath + '.sha256', "$hash  $releaseName.zip`n", [Text.Encoding]::ASCII)
    Write-Host "ZIP release: $zipPath"
    Write-Host ('Archive size: {0:N1} MiB' -f ((Get-Item -LiteralPath $zipPath).Length / 1MB))
} finally {
    if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
}
