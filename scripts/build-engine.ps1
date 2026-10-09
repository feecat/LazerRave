[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86')][string]$Architecture = 'x64',
    [ValidateSet('RelWithDebInfo', 'Release', 'Debug')][string]$Configuration = 'RelWithDebInfo',
    [ValidateRange(1, 64)][int]$Jobs = 4,
    [switch]$PrepareRuntime,
    [switch]$UpdateRuntime,
    [switch]$Fresh,
    [string]$RuntimeSource = '',
    [ValidateRange(320, 7680)][int]$WindowWidth = 1024,
    [ValidateRange(240, 4320)][int]$WindowHeight = 768,
    [switch]$DirectDownload
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$root = Join-Path $workspace 'src\OpenLR2'
if (!$RuntimeSource) { $RuntimeSource = Join-Path $workspace 'res\runtime' }
$toolsDir = Join-Path $workspace '.tools'
$envNames = @('PATH', 'VCPKG_ROOT', 'VCPKG_FORCE_SYSTEM_BINARIES', 'VCPKG_DISABLE_METRICS', 'VCPKG_KEEP_ENV_VARS', 'VCPKG_DOWNLOADS', 'VCPKG_DEFAULT_BINARY_CACHE', 'HTTP_PROXY', 'HTTPS_PROXY', 'NO_PROXY', 'VCPKG_ENV_PASSTHROUGH_UNTRACKED')
$savedEnv = @{}
foreach ($name in $envNames) { $savedEnv[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed (exit $LASTEXITCODE)." }
}

try {
    if ($PrepareRuntime -and $UpdateRuntime) { throw 'Choose either -PrepareRuntime or -UpdateRuntime.' }
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
    $cmakeCandidates = @(
        (Join-Path $toolsDir 'cmake-python\cmake\data\bin\cmake.exe')
    )
    $cmake = $cmakeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$cmake) {
        $command = Get-Command cmake.exe -ErrorAction SilentlyContinue
        if ($command) { $cmake = $command.Source }
    }
    if (!$cmake) { throw 'Install CMake 3.29 or newer, or place it on PATH.' }
    $cmakeVersionText = & $cmake --version
    if ($LASTEXITCODE -ne 0 -or $cmakeVersionText[0] -notmatch '(\d+\.\d+\.\d+)') { throw 'Unable to read CMake version.' }
    if ([version]$Matches[1] -lt [version]'3.29.0') { throw 'CMake 3.29 or newer is required.' }
    $env:PATH = "$(Split-Path $cmake -Parent);$env:PATH"
    $localNinja = Join-Path $toolsDir 'ninja-python\bin'
    if (Test-Path -LiteralPath (Join-Path $localNinja 'ninja.exe')) { $env:PATH = "$localNinja;$env:PATH" }
    $localNasm = Join-Path $toolsDir 'nasm-source'
    if (Test-Path -LiteralPath (Join-Path $localNasm 'nasm.exe')) { $env:PATH = "$localNasm;$env:PATH" }
    $pwshDir = Join-Path $env:ProgramFiles 'PowerShell\7'
    if (Test-Path -LiteralPath (Join-Path $pwshDir 'pwsh.exe')) { $env:PATH = "$pwshDir;$env:PATH" }

    $vcpkgRoot = Join-Path $toolsDir 'vcpkg'
    $registry = Get-Content -LiteralPath (Join-Path $root 'vcpkg-configuration.json') -Raw | ConvertFrom-Json
    $baseline = $registry.'default-registry'.baseline
    if (!(Test-Path -LiteralPath (Join-Path $vcpkgRoot '.git'))) {
        Invoke-Checked 'git.exe' @('clone', '--filter=blob:none', '--no-checkout', '--depth', '1', 'https://github.com/microsoft/vcpkg.git', $vcpkgRoot)
    }
    $head = & git.exe -C $vcpkgRoot rev-parse --verify --quiet HEAD
    if ($LASTEXITCODE -ne 0 -or $head -ne $baseline) {
        Invoke-Checked 'git.exe' @('-C', $vcpkgRoot, 'fetch', '--depth', '1', 'origin', $baseline)
        Invoke-Checked 'git.exe' @('-C', $vcpkgRoot, 'checkout', '--detach', $baseline)
    }
    $vcpkg = Join-Path $vcpkgRoot 'vcpkg.exe'
    if (!(Test-Path -LiteralPath $vcpkg)) {
        $metadata = Get-Content -LiteralPath (Join-Path $vcpkgRoot 'scripts\vcpkg-tool-metadata.txt')
        $tagLine = $metadata | Where-Object { $_ -match '^VCPKG_TOOL_RELEASE_TAG=' }
        $tag = ($tagLine -split '=', 2)[1]
        $download = "$vcpkg.download"
        Invoke-Checked 'curl.exe' @('--fail', '--location', '--retry', '2', '--connect-timeout', '30', '--max-time', '1800', '--output', $download, "https://github.com/microsoft/vcpkg-tool/releases/download/$tag/vcpkg.exe")
        Move-Item -LiteralPath $download -Destination $vcpkg -Force
    }
    Invoke-Checked $vcpkg @('version', '--disable-metrics')
    New-Item -ItemType File -Force -Path (Join-Path $vcpkgRoot 'vcpkg.disable-metrics') | Out-Null
    $env:VCPKG_ROOT = $vcpkgRoot
    $env:VCPKG_FORCE_SYSTEM_BINARIES = '1'
    $env:VCPKG_DISABLE_METRICS = '1'
    $env:VCPKG_KEEP_ENV_VARS = 'PATH'
    $dependencies = Join-Path $workspace 'out/deps/vcpkg'
    foreach ($folder in @('downloads', 'binary-cache', 'buildtrees', 'packages', 'installed')) {
        New-Item -ItemType Directory -Force -Path (Join-Path $dependencies $folder) | Out-Null
    }
    $env:VCPKG_DOWNLOADS = Join-Path $dependencies 'downloads'
    if (!$env:VCPKG_DEFAULT_BINARY_CACHE) { $env:VCPKG_DEFAULT_BINARY_CACHE = Join-Path $dependencies 'binary-cache' }
    if ($DirectDownload) {
        # Prevent vcpkg from importing the Windows IE proxy. NO_PROXY bypasses this placeholder.
        $env:HTTP_PROXY = 'http://127.0.0.1:9'
        $env:HTTPS_PROXY = $env:HTTP_PROXY
        $env:NO_PROXY = '*'
        $env:VCPKG_ENV_PASSTHROUGH_UNTRACKED = 'HTTP_PROXY;HTTPS_PROXY;NO_PROXY'
    }

    $preset = "windows-vs-$Architecture"
    $buildDir = Join-Path $workspace "out/build/engine/$preset"
    $configureArgs = @('--preset', $preset)
    if ($Architecture -eq 'x64' -and $Configuration -eq 'Debug') {
        $buildDir = Join-Path $workspace "out/build/engine/$preset-debug"
        $configureArgs += @('-DVCPKG_TARGET_TRIPLET=x64-windows-static')
    }
    $configureArgs += @('-B', $buildDir)
    $dependencyInstall = Join-Path $dependencies ('installed/' + (Split-Path $buildDir -Leaf))
    $configureArgs += "-DVCPKG_INSTALLED_DIR=$dependencyInstall"
    $configureArgs += "-DVCPKG_INSTALL_OPTIONS=--x-buildtrees-root=$(Join-Path $dependencies 'buildtrees');--x-packages-root=$(Join-Path $dependencies 'packages')"
    $cachePath = Join-Path $buildDir 'CMakeCache.txt'
    if (Test-Path -LiteralPath $cachePath) {
        $homeLine = Get-Content -LiteralPath $cachePath | Where-Object { $_ -like 'CMAKE_HOME_DIRECTORY:INTERNAL=*' } | Select-Object -First 1
        if ($homeLine -and [IO.Path]::GetFullPath(($homeLine -split '=', 2)[1]) -ne [IO.Path]::GetFullPath($root)) { $Fresh = $true }
    }
    if ($Fresh) { $configureArgs += '--fresh' }
    Push-Location $root
    try {
        Invoke-Checked $cmake $configureArgs
        Invoke-Checked $cmake @('--build', $buildDir, '--config', $Configuration, '--parallel', "$Jobs")
    } finally { Pop-Location }

    $artifacts = Join-Path $buildDir $Configuration
    $exeName = "OpenLR2_$Architecture.exe"
    $fmodDir = if ($Architecture -eq 'x64') { 'bin64' } else { 'bin86' }
    $fmodName = if ($Configuration -eq 'Debug') { 'fmodL.dll' } else { 'fmod.dll' }
    Copy-Item -LiteralPath (Join-Path $root "dep\FMOD\$fmodDir\$fmodName") -Destination $artifacts -Force
    Write-Host "Engine build output: $artifacts"

    if ($UpdateRuntime) {
        $runtime = Join-Path $workspace "out/reports/engine-runtime/$Architecture/$Configuration"
        if (!(Test-Path -LiteralPath (Join-Path $runtime 'LR2files/Config/config.xml'))) { throw 'Prepare the runtime before updating binaries.' }
        $activeEngine = @(Get-Process -Name "OpenLR2_$Architecture" -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $runtime $exeName) })
        if ($activeEngine.Count) { throw 'Close the game before updating its runtime binaries.' }
        foreach ($name in @($exeName, $fmodName, "OpenLR2_$Architecture.pdb")) {
            $artifact = Join-Path $artifacts $name
            if (Test-Path -LiteralPath $artifact) { Copy-Item -LiteralPath $artifact -Destination $runtime -Force }
        }
        Write-Host "Updated runtime binaries: $runtime"
    }

    if ($PrepareRuntime) {
        if (!(Test-Path -LiteralPath (Join-Path $RuntimeSource 'LR2files'))) { throw "Runtime source has no LR2files directory: $RuntimeSource" }
        $runtime = Join-Path $workspace "out/reports/engine-runtime/$Architecture/$Configuration"
        $sourceResolved = (Resolve-Path -LiteralPath $RuntimeSource).Path.TrimEnd('\')
        if ($sourceResolved -eq $runtime -or $runtime.StartsWith($sourceResolved + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime destination must be outside the source runtime directory.' }
        New-Item -ItemType Directory -Force -Path $runtime | Out-Null
        & robocopy.exe $sourceResolved $runtime /E /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XF 'OpenLR2*.exe' 'OpenLR2*.pdb' 'ExampleIR*.dll' 'ExampleIR*.pdb'
        if ($LASTEXITCODE -ge 8) { throw "Runtime copy failed (robocopy exit $LASTEXITCODE)." }
        foreach ($name in @($exeName, $fmodName, "OpenLR2_$Architecture.pdb")) {
            $artifact = Join-Path $artifacts $name
            if (Test-Path -LiteralPath $artifact) { Copy-Item -LiteralPath $artifact -Destination $runtime -Force }
        }
        # ExampleIR remains in the build output, outside the active runtime IR directory.
        $launcher = @('@echo off', 'cd /d "%~dp0"', "start `"OpenLR2`" `"%~dp0$exeName`" -ns")
        Set-Content -LiteralPath (Join-Path $runtime 'run-openlr2.cmd') -Value $launcher -Encoding ASCII
        $windowScript = Join-Path $workspace 'scripts/set-window.ps1'
        Copy-Item -LiteralPath $windowScript -Destination $runtime -Force
        & $windowScript -RuntimeDirectory $runtime -Width $WindowWidth -Height $WindowHeight
        foreach ($size in @(@(1024, 768), @(1920, 1080))) {
            $sizeLauncher = @(
                '@echo off',
                "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0set-window.ps1`" -RuntimeDirectory `"%~dp0.`" -Width $($size[0]) -Height $($size[1])",
                'if errorlevel 1 exit /b 1',
                'call "%~dp0run-openlr2.cmd"'
            )
            Set-Content -LiteralPath (Join-Path $runtime "run-$($size[0])x$($size[1]).cmd") -Value $sizeLauncher -Encoding ASCII
        }
        Write-Host "Independent runtime: $runtime"
    }
    $global:LASTEXITCODE = 0
} finally {
    foreach ($name in $envNames) { [Environment]::SetEnvironmentVariable($name, $savedEnv[$name], 'Process') }
}
