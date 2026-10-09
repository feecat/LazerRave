[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug', 'RelWithDebInfo')][string]$Configuration,
    [ValidateRange(1, 64)][int]$Jobs = 4,
    [switch]$CompileOnly,
    [switch]$BuildEngine,
    [string]$RuntimeSource = '',
    [string]$Destination = '',
    [switch]$EngineOnly,
    [ValidateSet('x64', 'x86')][string]$Architecture = 'x64',
    [switch]$PrepareRuntime,
    [switch]$UpdateRuntime,
    [switch]$Fresh,
    [ValidateRange(320, 7680)][int]$WindowWidth = 1024,
    [ValidateRange(240, 4320)][int]$WindowHeight = 768,
    [switch]$DirectDownload
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$savedBuildEnvironment = @{}
foreach ($name in @('DOTNET_CLI_UI_LANGUAGE', 'NUGET_CLI_LANGUAGE', 'VSLANG', 'PreferredUILang', 'TEMP', 'TMP', 'TMPDIR')) {
    $savedBuildEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$savedConsoleInput = [Console]::InputEncoding
$savedConsoleOutput = [Console]::OutputEncoding
$savedPipelineEncoding = $OutputEncoding
$savedUICulture = [Threading.Thread]::CurrentThread.CurrentUICulture
try {
    $env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
    $env:NUGET_CLI_LANGUAGE = 'en-US'
    $env:VSLANG = '1033'
    $env:PreferredUILang = 'en-US'
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [Console]::InputEncoding = $utf8
    [Console]::OutputEncoding = $utf8
    $OutputEncoding = $utf8
    [Threading.Thread]::CurrentThread.CurrentUICulture = [Globalization.CultureInfo]::GetCultureInfo('en-US')

    $engineOptions = @('Architecture', 'PrepareRuntime', 'UpdateRuntime', 'Fresh', 'WindowWidth', 'WindowHeight', 'DirectDownload')
    if (!$EngineOnly) {
        if (@($engineOptions | Where-Object { $PSBoundParameters.ContainsKey($_) }).Count) {
            throw 'Engine-specific options require -EngineOnly.'
        }
        if ($Configuration -eq 'RelWithDebInfo') { throw 'The client supports Release or Debug.' }
    } elseif ($CompileOnly -or $BuildEngine -or $Destination) {
        throw '-CompileOnly, -BuildEngine and -Destination apply to the complete client.'
    }
    if (!$Configuration) { $Configuration = if ($EngineOnly) { 'RelWithDebInfo' } else { 'Release' } }
    $logDirectory = Join-Path $workspace 'out/logs'
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    $log = Join-Path $logDirectory ('build-lazerrave-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
    $global:LASTEXITCODE = 0
    try {
        $buildTempDirectory = Join-Path $workspace 'out/build/temp'
        [IO.Directory]::CreateDirectory($buildTempDirectory) | Out-Null
        $probe = Join-Path $buildTempDirectory ('write-probe-' + [Guid]::NewGuid().ToString('N'))
        try {
            [IO.File]::WriteAllText($probe, '')
        } catch {
            throw "Build temporary directory is not writable: $buildTempDirectory. $($_.Exception.Message)"
        } finally {
            if ([IO.File]::Exists($probe)) { [IO.File]::Delete($probe) }
        }
        $env:TEMP = $buildTempDirectory
        $env:TMP = $buildTempDirectory
        $env:TMPDIR = $buildTempDirectory
        "Build temporary directory: $buildTempDirectory" | Tee-Object -FilePath $log
        if ($EngineOnly) {
            $script = Join-Path $PSScriptRoot 'build-engine.ps1'
            $arguments = @{
                Configuration = $Configuration
                Jobs = $Jobs
                RuntimeSource = $RuntimeSource
            }
            foreach ($name in $engineOptions) {
                if ($PSBoundParameters.ContainsKey($name)) { $arguments[$name] = $PSBoundParameters[$name] }
            }
        } else {
            $script = Join-Path $PSScriptRoot 'build-client.ps1'
            $arguments = @{
                Configuration = $Configuration
                EngineJobs = $Jobs
                CompileOnly = $CompileOnly
                BuildEngine = $BuildEngine
                RuntimeSource = $RuntimeSource
                Destination = $Destination
            }
        }
        & $script @arguments *>&1 | Tee-Object -FilePath $log -Append
        if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
    } catch {
        $_ | Out-String | Add-Content -LiteralPath $log
        Write-Host "Build failed. Log: $log"
        throw
    }
    Write-Host "Build log: $log"
} finally {
    foreach ($name in $savedBuildEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedBuildEnvironment[$name], 'Process')
    }
    [Console]::InputEncoding = $savedConsoleInput
    [Console]::OutputEncoding = $savedConsoleOutput
    $OutputEncoding = $savedPipelineEncoding
    [Threading.Thread]::CurrentThread.CurrentUICulture = $savedUICulture
}
