<#
.SYNOPSIS
    Cai bo gh alias cho repo AioKin.

.DESCRIPTION
    LUU Y: `gh alias` la cau hinh CUA MAY, khong phai cua repo - no nam trong
    ~/.config/gh/config.yml va co mat o moi repo ban mo. Vi vay moi alias o day deu mang
    tien to `aiokin-` va deu ghi ro `--repo`, de chung khong bao gio lo hanh dong nham sang
    mot repo khac khi ban dang dung o thu muc khac.

    Ban PowerShell nay va scripts/gh-aliases.sh phai giu CUNG mot bo alias. Sua mot ben thi
    sua ca ben kia - lech nhau nghia la hai nguoi trong nhom go cung mot lenh ra hai ket qua.

.EXAMPLE
    .\scripts\gh-aliases.ps1
    .\scripts\gh-aliases.ps1 -List
    .\scripts\gh-aliases.ps1 -Remove
#>
[CmdletBinding()]
param(
    [switch]$List,
    [switch]$Remove
)

$ErrorActionPreference = "Stop"

$Repo = "NTPhong04102k4/AioKin"

# Alias bat dau bang "!" la shell alias (gh chay qua `sh -c`; Git for Windows da kem san sh).
#
# Cac alias goi script trong repo deu `cd` ve goc repo truoc: chung dung duong dan tuong
# doi, con alias thi co the duoc go tu bat ky thu muc con nao.
#
# [ordered] de thu tu hien ra on dinh giua cac lan chay - de doi chieu voi ban .sh.
$Aliases = [ordered]@{
    # --- Vong doi mot thay doi (docs/git-flow.md muc 4) ---

    # Tach nhanh moi tu main moi nhat. Tranh duoc loi hay gap nhat: tach nhanh tu mot main
    # cu ca tuan, roi PR keo theo ca dong commit khong phai cua minh.
    "aiokin-new"       = '!set -e; cd "$(git rev-parse --show-toplevel)"; test -n "${1:-}" || { echo "Dung: gh aiokin-new <type>/<mo-ta-ngan>" >&2; exit 1; }; git switch main; git pull --ff-only; git switch -c "$1"'

    # Dong bo nhanh hien tai len origin/main bang rebase, kem cac kiem tra an toan.
    "aiokin-sync"      = '!cd "$(git rev-parse --show-toplevel)" && ./scripts/git-sync.sh "$@"'

    # Sau khi PR da merge: ve main, pull, xoa nhanh o ca local lan remote.
    "aiokin-land"      = '!cd "$(git rev-parse --show-toplevel)" && ./scripts/git-land.sh "$@"'

    # Cong kiem tra truoc khi mo PR (docs/git-flow.md muc 7).
    "aiokin-check"     = '!cd "$(git rev-parse --show-toplevel)" && dotnet build AioKin.sln -c Release'

    # --- Pull request ---

    # Day nhanh hien tai len roi mo PR. --fill lay tieu de/mo ta tu commit, nen commit dat
    # theo Conventional Commits la PR cung dung dinh dang luon.
    "aiokin-pr-new"    = '!set -e; cd "$(git rev-parse --show-toplevel)"; git push -u origin HEAD; gh pr create --fill --base main "$@"'

    "aiokin-prs"       = "pr list --repo $Repo"
    "aiokin-mine"      = "pr list --repo $Repo --author @me"

    # PR cua nhanh dang dung: xem tren web / theo doi CI cho toi khi co ket qua.
    "aiokin-pr-web"    = "pr view --repo $Repo --web"
    "aiokin-pr-checks" = "pr checks --repo $Repo --watch"

    # --- CI ---

    "aiokin-ci"        = "run list --repo $Repo --limit 10"
    "aiokin-ci-fail"   = "run list --repo $Repo --status failure --limit 10"

    # Log cua lan chay CI moi nhat, chi phan that bai - thu duy nhat dang doc khi CI do.
    # Chua co lan chay nao thi --jq tra chuoi rong, va `gh run view ""` se bao 404 kho hieu;
    # chan som de thong bao noi dung su that.
    "aiokin-ci-log"    = '!set -e; id="$(gh run list --repo NTPhong04102k4/AioKin --limit 1 --json databaseId --jq ".[0].databaseId")"; test -n "$id" || { echo "Chua co lan chay CI nao cho repo nay." >&2; exit 1; }; gh run view --repo NTPhong04102k4/AioKin "$id" --log-failed'
}

if ($List) {
    $installed = gh alias list | Select-String -Pattern "^aiokin"
    if ($installed) { $installed.Line } else { Write-Host "Chua co alias aiokin- nao." }
    exit 0
}

if ($Remove) {
    foreach ($name in $Aliases.Keys) {
        # Alias chua duoc cai thi `gh alias delete` bao loi - o day khong phai van de, nen
        # nuot no thay vi dung ca vong lap.
        try { gh alias delete $name 2>$null; if ($?) { Write-Host "da go $name" } } catch { }
    }
    exit 0
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Error "Chua cai gh CLI: https://cli.github.com"
    exit 1
}

gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Error "gh chua dang nhap. Chay: gh auth login"
    exit 1
}

# Windows PowerShell 5.1 dung command line cho native exe theo quy tac cua no, va dau nhay
# kep BEN TRONG mot tham so phai duoc escape thanh \" - khong lam thi gh nhan mot chuoi da
# vo doi va bao "unknown flag: --show-toplevel". Cac phan mo rong o tren deu chua dau nhay
# kep, nen buoc nay la bat buoc chu khong phai phong xa.
#
# Dua qua STDIN (`gh alias set <ten> -`) thi tranh duoc chuyen escape, nhung pipeline cua
# PowerShell them mot dong moi vao cuoi va gh giu no nguyen trong alias - alias thuong bi
# tu choi ngay, con shell alias thi luu duoc va hong luc chay. Nen dung escape.
#
# --clobber de chay lai la cap nhat, khong phai bao trung ten: script nay duoc chay moi lan
# bo alias thay doi.
$failed = @()

foreach ($name in $Aliases.Keys) {
    $expansion = $Aliases[$name]
    $isShell = $expansion.StartsWith("!")
    if ($isShell) { $expansion = $expansion.Substring(1) }

    $escaped = $expansion -replace '"', '\"'

    if ($isShell) {
        gh alias set $name $escaped --shell --clobber
    }
    else {
        gh alias set $name $escaped --clobber
    }

    if ($LASTEXITCODE -ne 0) { $failed += $name }
}

if ($failed.Count -gt 0) {
    # Bao that bai to tieng thay vi de script ket thuc bang dong "Xong" mau xanh: mot alias
    # khong duoc cai ma nguoi dung tuong la co se lo ra vao luc ho dang can no nhat.
    Write-Error ("Khong cai duoc " + $failed.Count + " alias: " + ($failed -join ", "))
    exit 1
}

Write-Host ""
Write-Host "Xong. Xem lai bang: .\scripts\gh-aliases.ps1 -List" -ForegroundColor Green
