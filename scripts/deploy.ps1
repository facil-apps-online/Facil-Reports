# Facil Reports - Deploy Script (Windows)
# Transfers code via SCP and rebuilds Docker container on the server
# Usage: .\scripts\deploy.ps1 [-Server 142.93.126.166] [-Key ~\.ssh\deploy_facil_reports]

param(
    [string]$Server = "142.93.126.166",
    [string]$Key = "$env:USERPROFILE\.ssh\deploy_facil_reports",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path "$PSScriptRoot\..").Path
$RemoteDir = "/opt/FacilReports"
$TempTar = Join-Path $env:TEMP "facilreports.tar.gz"

Write-Host "=========================================="
Write-Host "  Facil Reports - Deploy (SCP)"
Write-Host "=========================================="

if (-not (Test-Path $Key)) {
    throw "SSH key not found: $Key"
}

# 1. Verify SSH connection
Write-Host "[1/4] Verifying SSH connection..."
& ssh -o BatchMode=yes -o ConnectTimeout=10 -i $Key "root@${Server}" "echo SSH-OK"
if ($LASTEXITCODE -ne 0) {
    throw "SSH connection failed"
}

# 2. Create tarball excluding git, bin, obj, .env
Write-Host "[2/4] Creating tarball..."
Push-Location $ProjectRoot
try {
    & tar -czf $TempTar `
        --exclude=".git" `
        --exclude="bin" `
        --exclude="obj" `
        --exclude=".env" `
        --exclude="node_modules" `
        --exclude="FacilReports/Reports/vault" `
        .
    if ($LASTEXITCODE -ne 0) { throw "tar failed" }
} finally {
    Pop-Location
}

# 3. Transfer and extract on server
Write-Host "[3/4] Transferring to ${Server}:$RemoteDir ..."
& scp -o BatchMode=yes -i $Key $TempTar "root@${Server}:/tmp/facilreports.tar.gz"
if ($LASTEXITCODE -ne 0) { throw "scp failed" }

& ssh -o BatchMode=yes -i $Key "root@${Server}" "mkdir -p $RemoteDir && tar -xzf /tmp/facilreports.tar.gz -C $RemoteDir && rm -f /tmp/facilreports.tar.gz"
if ($LASTEXITCODE -ne 0) { throw "extract failed" }

# 4. Build and start container
if ($SkipBuild) {
    Write-Host "[4/4] Skipping Docker build (SkipBuild)."
} else {
    Write-Host "[4/4] Building Docker image and starting container..."
    & ssh -o BatchMode=yes -i $Key "root@${Server}" "cd $RemoteDir && docker compose build --no-cache && docker compose up -d"
    if ($LASTEXITCODE -ne 0) { throw "docker build/up failed" }
}

Remove-Item $TempTar -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "=========================================="
Write-Host "  Deploy Complete!"
Write-Host "=========================================="
Write-Host ""
Write-Host "Health: https://reports.facil-apps.online/api/health"
