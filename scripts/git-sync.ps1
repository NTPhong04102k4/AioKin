<#
.SYNOPSIS
    Dong bo nhanh hien tai voi origin/main bang rebase.

.EXAMPLE
    .\scripts\git-sync.ps1
    .\scripts\git-sync.ps1 -Base develop

.NOTES
    Xem docs/git-flow.md muc 3 de biet vi sao la rebase chu khong phai merge.
#>
[CmdletBinding()]
param(
    [string]$Base = "main"
)

$ErrorActionPreference = "Stop"

$current = (git rev-parse --abbrev-ref HEAD).Trim()

if ($current -eq "HEAD") {
    Write-Error "Dang o trang thai detached HEAD - checkout mot nhanh truoc da."
    exit 1
}

if ($current -eq $Base) {
    # Rebase main len chinh no khong co y nghia, va neu main da lech thi day la luc
    # can pull chu khong phai viet lai lich su cua nhanh chung.
    Write-Error "Dang dung tren '$Base'. Dung 'git pull --ff-only' thay vi rebase."
    exit 1
}

if (git status --porcelain) {
    git status --short
    Write-Error "Working tree con thay doi chua commit. Commit hoac 'git stash' truoc khi rebase."
    exit 1
}

Write-Host "==> Fetch origin" -ForegroundColor Cyan
git fetch origin --prune

Write-Host "==> Rebase '$current' len 'origin/$Base'" -ForegroundColor Cyan
git rebase "origin/$Base"

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Rebase dung lai vi xung dot." -ForegroundColor Yellow
    Write-Host "  - Sua file bi xung dot, roi: git add <file>; git rebase --continue"
    Write-Host "  - Muon quay lai nguyen trang:  git rebase --abort"
    exit 1
}

$ahead = (git rev-list --count "origin/$Base..HEAD").Trim()
Write-Host ""
Write-Host "Xong. '$current' dang o tren 'origin/$Base', hon $ahead commit." -ForegroundColor Green

git rev-parse --verify --quiet "origin/$current" | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Nhanh nay da co tren remote va lich su vua bi viet lai. Day len bang:"
    Write-Host "    git push --force-with-lease" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "(--force-with-lease chu khong phai --force: neu co nguoi vua day them commit"
    Write-Host " len '$current', lenh se tu choi thay vi xoa mat cong cua ho.)"
}
