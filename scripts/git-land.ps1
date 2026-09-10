<#
.SYNOPSIS
    Don dep sau khi PR da duoc merge tren GitHub.

.DESCRIPTION
    Ve nhanh goc, pull, roi xoa nhanh tinh nang da xong o ca local lan remote.

.EXAMPLE
    .\scripts\git-land.ps1
    .\scripts\git-land.ps1 -Base develop
#>
[CmdletBinding()]
param(
    [string]$Base = "main"
)

$ErrorActionPreference = "Stop"

$current = (git rev-parse --abbrev-ref HEAD).Trim()

if ($current -eq $Base) {
    Write-Error "Dang dung tren '$Base' - khong co nhanh tinh nang nao de don."
    exit 1
}

if (git status --porcelain) {
    git status --short
    Write-Error "Working tree con thay doi chua commit. Xu ly truoc khi doi nhanh."
    exit 1
}

Write-Host "==> Ve '$Base' va cap nhat" -ForegroundColor Cyan
git switch $Base
git fetch origin --prune

# --ff-only: neu khong tua thang duoc thi nhanh goc cuc bo da lech - dung lai de nguoi
# dung xu ly, thay vi tu tao mot commit merge khong ai mong doi.
git pull --ff-only
if ($LASTEXITCODE -ne 0) {
    Write-Error "'$Base' cuc bo da lech khoi remote - xu ly thu cong."
    exit 1
}

# Squash merge tao ra commit moi, khong phai commit cua nhanh - nen 'git branch -d' se
# bao "chua merge". Doi chieu bang noi dung cay thu muc thay vi bang lich su.
$mergeBase = (git merge-base $Base $current).Trim()
$branchTree = (git rev-parse "$current^{tree}").Trim()
$squashed = (git commit-tree $branchTree -p $mergeBase -m "_").Trim()

$unmerged = git cherry $Base $squashed | Where-Object { $_ -like "+*" }

if (-not $unmerged) {
    Write-Host "==> '$current' da nam trong '$Base' (squash merge) - xoa" -ForegroundColor Cyan
    git branch -D $current
}
else {
    Write-Host "'$current' chua nam trong '$Base'. Khong xoa." -ForegroundColor Yellow
    Write-Host "Kiem tra PR da merge chua; neu chac chan roi thi: git branch -D $current"
    exit 1
}

git rev-parse --verify --quiet "origin/$current" | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "==> Xoa nhanh tren remote" -ForegroundColor Cyan
    git push origin --delete $current
}

Write-Host ""
Write-Host "Xong. Dang o '$Base' tai $((git rev-parse --short HEAD).Trim())." -ForegroundColor Green
