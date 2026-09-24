#!/usr/bin/env bash
#
# Don dep sau khi PR da duoc merge tren GitHub.
#
#   scripts/git-land.sh [nhanh-goc]     # mac dinh: main
#
# Ve nhanh goc, pull, roi xoa nhanh tinh nang da xong o ca local lan remote.

set -euo pipefail

BASE="${1:-main}"
CURRENT="$(git rev-parse --abbrev-ref HEAD)"

if [ "$CURRENT" = "$BASE" ]; then
    echo "Dang dung tren '$BASE' - khong co nhanh tinh nang nao de don." >&2
    exit 1
fi

if [ -n "$(git status --porcelain)" ]; then
    echo "Working tree con thay doi chua commit. Xu ly truoc khi doi nhanh." >&2
    git status --short >&2
    exit 1
fi

echo "==> Ve '$BASE' va cap nhat"
git switch "$BASE"
git fetch origin --prune

# --ff-only: neu khong tua thang duoc thi nhanh goc cuc bo da lech - dung lai de nguoi
# dung xu ly, thay vi tu tao mot commit merge khong ai mong doi.
git pull --ff-only

# Squash merge tao ra commit moi, khong phai commit cua nhanh - nen 'git branch -d'
# se bao "chua merge". Doi chieu bang noi dung cay thu muc thay vi bang lich su.
MERGE_BASE="$(git merge-base "$BASE" "$CURRENT")"
BRANCH_TREE="$(git rev-parse "$CURRENT^{tree}")"
SQUASHED_COMMIT="$(git commit-tree "$BRANCH_TREE" -p "$MERGE_BASE" -m _ 2>/dev/null || true)"

if [ -n "$SQUASHED_COMMIT" ] && [ -z "$(git cherry "$BASE" "$SQUASHED_COMMIT" | grep '^+' || true)" ]; then
    echo "==> '$CURRENT' da nam trong '$BASE' (squash merge) - xoa"
    git branch -D "$CURRENT"
elif git branch --merged "$BASE" | grep -qx "  $CURRENT"; then
    echo "==> '$CURRENT' da duoc merge - xoa"
    git branch -d "$CURRENT"
else
    echo "'$CURRENT' chua nam trong '$BASE'. Khong xoa." >&2
    echo "Kiem tra PR da merge chua; neu chac chan roi thi: git branch -D $CURRENT" >&2
    exit 1
fi

if git rev-parse --verify --quiet "origin/$CURRENT" >/dev/null; then
    echo "==> Xoa nhanh tren remote"
    git push origin --delete "$CURRENT"
fi

echo
echo "Xong. Dang o '$BASE' tai $(git rev-parse --short HEAD)."
