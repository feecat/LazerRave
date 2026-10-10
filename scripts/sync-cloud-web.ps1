# Frontend only: .\scripts\sync-cloud-web.ps1 -Server deploy@HOST -RemoteRoot /srv/lazerrave
# Bundles with Vite directly; no type checking, tests, health checks or Git commands.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+@[A-Za-z0-9_.-]+$')][string]$Server,
    [string]$IdentityFile = '',
    [Parameter(Mandatory)][ValidatePattern('^/[A-Za-z0-9_/-]+$')][string]$RemoteRoot
)

$ErrorActionPreference = 'Stop'
if ($RemoteRoot.Trim('/') -eq '') { throw 'RemoteRoot must be a deployment directory, not the filesystem root.' }
$RemoteRoot = $RemoteRoot.TrimEnd('/')
if ($IdentityFile) {
    if (!(Test-Path -LiteralPath $IdentityFile -PathType Leaf)) { throw 'The specified SSH identity file does not exist.' }
    $IdentityFile = (Resolve-Path -LiteralPath $IdentityFile).Path
}
$workspace = Split-Path $PSScriptRoot -Parent
$webDirectory = Join-Path $workspace 'src/Cloud/web'
$dist = Join-Path $workspace 'out/build/cloud/web/dist'
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$archive = Join-Path $workspace "out/build/cloud/web/frontend-$stamp.tar.gz"
$remoteUpload = "$RemoteRoot/.web-sync-$stamp"
$sshOptions = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15')
if ($IdentityFile) { $sshOptions = @('-i', $IdentityFile) + $sshOptions }

Push-Location $webDirectory
try {
    # Deliberately bypass npm run build, which also runs tsc --noEmit.
    & node (Join-Path $webDirectory 'node_modules/vite/bin/vite.js') build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend bundle failed.' }
} finally {
    Pop-Location
}

& tar -czf $archive -C $dist .
if ($LASTEXITCODE -ne 0) { throw 'Frontend archive failed.' }
& ssh @sshOptions $Server "mkdir -p '$remoteUpload'"
if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare upload directory.' }
& scp @sshOptions $archive "${Server}:$remoteUpload/frontend.tar.gz"
if ($LASTEXITCODE -ne 0) { throw 'Frontend upload failed.' }

$remoteScript = @'
set -eu
root="$1"
upload="$2"
stamp="$3"
cd "$root"
mkdir -p "$upload/site" "$root/backups/web"
tar -xzf "$upload/frontend.tar.gz" -C "$upload/site"
chmod -R a+rX "$upload/site"
tar -czf "$root/backups/web/$stamp.tar.gz" -C "$root/server/wwwroot" .

# Preserve previous hashed assets for browser tabs still using the old page.
cp -a "$upload/site/." "$root/server/wwwroot/"
chmod -R a+rX "$root/server/wwwroot"
# Save the same frontend into the image for future container recreation.
# Existing backend binaries and running containers are retained.
docker compose build api
container=$(docker compose ps -q api)
if [ -z "$container" ]; then
    echo 'No running API container available for frontend sync.' >&2
    exit 1
fi

# Publish the entry page last so its assets are already available.
mv "$upload/site/index.html" "$upload/index.html"
docker cp "$upload/site/." "$container:/app/wwwroot/"
docker exec -u 0 "$container" chmod -R a+rX /app/wwwroot
docker cp "$upload/index.html" "$container:/app/wwwroot/index.html"
docker exec -u 0 "$container" chmod a+r /app/wwwroot/index.html
echo "Frontend synced. Previous static files: $root/backups/web/$stamp.tar.gz"
'@
$remoteScript.Replace("`r`n", "`n") | & ssh @sshOptions $Server "sh -s -- '$RemoteRoot' '$remoteUpload' '$stamp'"
if ($LASTEXITCODE -ne 0) { throw 'Remote frontend sync failed; uploaded files and static backup have been retained.' }
Write-Host 'Frontend sync completed. No checks or Git commands were run.'
