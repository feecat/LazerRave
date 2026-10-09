[CmdletBinding()]
param(
    [ValidateRange(320, 7680)][int]$Width = 1024,
    [ValidateRange(240, 4320)][int]$Height = 768,
    [string]$RuntimeDirectory = ''
)
$ErrorActionPreference = 'Stop'
if (!$RuntimeDirectory) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'LR2files/Config/config.xml')) { $RuntimeDirectory = $PSScriptRoot }
    else { $RuntimeDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'out/app' }
}
$RuntimeDirectory = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$active = @(Get-Process -Name 'OpenLR2*' -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and (Split-Path $_.Path -Parent) -eq $RuntimeDirectory
})
if ($active.Count) { throw 'Close OpenLR2 in this runtime directory before changing its settings.' }
$configPath = Join-Path $RuntimeDirectory 'LR2files\Config\config.xml'
$openConfigPath = Join-Path $RuntimeDirectory 'LR2files\Config\openlr2-config.xml'
$encoding = [System.Text.Encoding]::GetEncoding(932)

function Set-SystemValue([string]$Text, [string]$Name, [int]$Value) {
    $systemMatch = [regex]::Match($Text, '(?s)<system>.*?</system>')
    if (!$systemMatch.Success) { throw 'Configuration has no system section.' }
    $system = $systemMatch.Value
    $pattern = '<' + $Name + '>[^<]*</' + $Name + '>'
    $replacement = '<' + $Name + '>' + $Value + '</' + $Name + '>'
    if ([regex]::IsMatch($system, $pattern)) { $system = [regex]::Replace($system, $pattern, $replacement) }
    else { $system = $system.Replace('</system>', "`t$replacement`r`n`t</system>") }
    return $Text.Substring(0, $systemMatch.Index) + $system + $Text.Substring($systemMatch.Index + $systemMatch.Length)
}

$config = [System.IO.File]::ReadAllText($configPath, $encoding)
$config = Set-SystemValue $config 'screenmode' 1
$config = Set-SystemValue $config 'windowsize_x' $Width
$config = Set-SystemValue $config 'windowsize_y' $Height
if (Test-Path -LiteralPath $openConfigPath) { $openConfig = [System.IO.File]::ReadAllText($openConfigPath, $encoding) }
else { $openConfig = "<?xml version=`"1.0`" encoding=`"shift_jis`"?>`r`n<config>`r`n`t<system></system>`r`n</config>`r`n" }
$openConfig = Set-SystemValue $openConfig 'screenmode' 1
# Keep the skin's existing render resolution; change only the window and scaling.
[xml]$config | Out-Null
[xml]$openConfig | Out-Null
if (!(Test-Path -LiteralPath "$configPath.before-window.bak")) { Copy-Item -LiteralPath $configPath -Destination "$configPath.before-window.bak" }
if ((Test-Path -LiteralPath $openConfigPath) -and !(Test-Path -LiteralPath "$openConfigPath.before-window.bak")) { Copy-Item -LiteralPath $openConfigPath -Destination "$openConfigPath.before-window.bak" }
[System.IO.File]::WriteAllText($configPath, $config, $encoding)
[System.IO.File]::WriteAllText($openConfigPath, $openConfig, $encoding)
Write-Host "Windowed $Width x $Height : $RuntimeDirectory"
