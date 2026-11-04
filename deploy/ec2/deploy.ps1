# One-command deploy from your Windows PC to the EC2 server.
# It: packs the code -> copies code + .env to EC2 -> installs Docker (first time) -> builds -> starts -> checks /health.
#
# Usage (PowerShell, from anywhere):
#   .\deploy\ec2\deploy.ps1
#   .\deploy\ec2\deploy.ps1 -HostIp 13.127.229.166      # when the EC2 public IP changed
param(
    [string]$HostIp = "13.127.229.166",
    [string]$KeyPath = "$HOME\Downloads\routego-key.pem",
    [string]$User = "ec2-user"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$envFile = Join-Path $PSScriptRoot ".env"
$archive = Join-Path $env:TEMP "notification.tar.gz"
$target = "$User@$HostIp"

function Assert-LastExit([string]$step) {
    if ($LASTEXITCODE -ne 0) { throw "FAILED at step: $step (exit code $LASTEXITCODE)" }
}

if (-not (Test-Path $KeyPath)) { throw "Key file not found: $KeyPath  (pass -KeyPath <path to .pem>)" }
if (-not (Test-Path $envFile)) { throw "Missing $envFile  (copy .env.example to .env and fill it in)" }

Write-Host "1/4 Packing code from $repoRoot" -ForegroundColor Cyan
tar.exe -czf $archive --exclude=bin --exclude=obj --exclude=.git --exclude=.vs --exclude=.env -C $repoRoot .
Assert-LastExit "pack code"

Write-Host "2/4 Copying code and .env to $target" -ForegroundColor Cyan
scp -i $KeyPath -o StrictHostKeyChecking=accept-new $archive "${target}:~/notification.tar.gz"
Assert-LastExit "copy code"
scp -i $KeyPath $envFile "${target}:~/notification.env"
Assert-LastExit "copy .env"

Write-Host "3/4 Unpacking and setting up server" -ForegroundColor Cyan
$remote = "rm -rf ~/notification && mkdir -p ~/notification" +
    " && tar -xzf ~/notification.tar.gz -C ~/notification" +
    " && mv ~/notification.env ~/notification/deploy/ec2/.env && chmod 600 ~/notification/deploy/ec2/.env" +
    " && sudo bash ~/notification/deploy/ec2/setup-server.sh"
ssh -i $KeyPath $target $remote
Assert-LastExit "server setup"

Write-Host "4/4 Building and starting containers" -ForegroundColor Cyan
ssh -i $KeyPath $target "sudo bash ~/notification/deploy/ec2/start.sh"
Assert-LastExit "build and start"

Write-Host "DONE. Test: http://${HostIp}:5007/health (needs port 5007 open for My IP)" -ForegroundColor Green
