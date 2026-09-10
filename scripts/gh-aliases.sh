#!/usr/bin/env bash
#
# Cai bo gh alias cho repo AioKin.
#
#   scripts/gh-aliases.sh              # cai / cap nhat
#   scripts/gh-aliases.sh --list       # xem cai gi dang duoc cai
#   scripts/gh-aliases.sh --remove     # go het
#
# LUU Y: `gh alias` la cau hinh CUA MAY, khong phai cua repo — no nam trong
# ~/.config/gh/config.yml va co mat o moi repo ban mo. Vi vay moi alias o day deu mang
# tien to `aiokin-` va deu ghi ro `--repo`, de chung khong bao gio lo hanh dong nham
# sang mot repo khac khi ban dang dung o thu muc khac.
#
# Script nay la nguon su that: sua o day roi chay lai, dung `gh alias set` bang tay —
# nhu the may thu hai cua ban va nguoi moi vao du an co cung bo lenh.

set -euo pipefail

REPO="NTPhong04102k4/AioKin"

# Ten alias -> phan mo rong. Alias bat dau bang "!" la shell alias (gh chay qua `sh -c`).
#
# Cac alias goi script trong repo deu `cd` ve goc repo truoc: chung dung duong dan tuong
# doi, con alias thi co the duoc go tu bat ky thu muc con nao.
declare -A ALIASES=(
    # --- Vong doi mot thay doi (docs/git-flow.md muc 4) ---

    # Tach nhanh moi tu main moi nhat. Tranh duoc loi hay gap nhat: tach nhanh tu mot main
    # cu ca tuan, roi PR keo theo ca dong commit khong phai cua minh.
    [aiokin-new]='!set -e; cd "$(git rev-parse --show-toplevel)"; test -n "${1:-}" || { echo "Dung: gh aiokin-new <type>/<mo-ta-ngan>" >&2; exit 1; }; git switch main; git pull --ff-only; git switch -c "$1"'

    # Dong bo nhanh hien tai len origin/main bang rebase, kem cac kiem tra an toan.
    [aiokin-sync]='!cd "$(git rev-parse --show-toplevel)" && ./scripts/git-sync.sh "$@"'

    # Sau khi PR da merge: ve main, pull, xoa nhanh o ca local lan remote.
    [aiokin-land]='!cd "$(git rev-parse --show-toplevel)" && ./scripts/git-land.sh "$@"'

    # Cong kiem tra truoc khi mo PR (docs/git-flow.md muc 7).
    [aiokin-check]='!cd "$(git rev-parse --show-toplevel)" && dotnet build AioKin.sln -c Release'

    # --- Pull request ---

    # Day nhanh hien tai len roi mo PR. --fill lay tieu de/mo ta tu commit, nen commit dat
    # theo Conventional Commits la PR cung dung dinh dang luon.
    [aiokin-pr-new]='!set -e; cd "$(git rev-parse --show-toplevel)"; git push -u origin HEAD; gh pr create --fill --base main "$@"'

    [aiokin-prs]="pr list --repo $REPO"
    [aiokin-mine]="pr list --repo $REPO --author @me"

    # PR cua nhanh dang dung: xem tren web / theo doi CI cho toi khi co ket qua.
    [aiokin-pr-web]="pr view --repo $REPO --web"
    [aiokin-pr-checks]="pr checks --repo $REPO --watch"

    # --- CI ---

    [aiokin-ci]="run list --repo $REPO --limit 10"
    [aiokin-ci-fail]="run list --repo $REPO --status failure --limit 10"

    # Log cua lan chay CI moi nhat, chi phan that bai — thu duy nhat dang doc khi CI do.
    # Chua co lan chay nao thi --jq tra chuoi rong, va `gh run view ""` se bao 404 kho hieu;
    # chan som de thong bao noi dung su that.
    [aiokin-ci-log]='!set -e; id="$(gh run list --repo NTPhong04102k4/AioKin --limit 1 --json databaseId --jq ".[0].databaseId")"; test -n "$id" || { echo "Chua co lan chay CI nao cho repo nay." >&2; exit 1; }; gh run view --repo NTPhong04102k4/AioKin "$id" --log-failed'
)

case "${1:-install}" in
    --list)
        gh alias list | grep '^aiokin' || echo "Chua co alias aiokin- nao."
        exit 0
        ;;
    --remove)
        for name in "${!ALIASES[@]}"; do
            gh alias delete "$name" 2>/dev/null && echo "da go $name" || true
        done
        exit 0
        ;;
    install) ;;
    *)
        echo "Tham so khong hieu: $1. Dung: [--list|--remove]" >&2
        exit 1
        ;;
esac

command -v gh >/dev/null || { echo "Chua cai gh CLI: https://cli.github.com" >&2; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "gh chua dang nhap. Chay: gh auth login" >&2; exit 1; }

# --clobber de chay lai la cap nhat, khong phai bao trung ten: script nay duoc chay moi
# lan bo alias thay doi.
for name in "${!ALIASES[@]}"; do
    expansion="${ALIASES[$name]}"

    if [[ "$expansion" == '!'* ]]; then
        gh alias set "$name" "${expansion:1}" --shell --clobber
    else
        gh alias set "$name" "$expansion" --clobber
    fi
done

echo
echo "Xong. Xem lai bang: scripts/gh-aliases.sh --list"
