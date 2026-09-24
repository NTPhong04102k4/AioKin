#!/usr/bin/env bash
#
# Dong bo nhanh hien tai voi origin/main bang rebase.
#
#   scripts/git-sync.sh [nhanh-goc]     # mac dinh: main
#
# Xem docs/git-flow.md muc 3 de biet vi sao la rebase chu khong phai merge.

set -euo pipefail

BASE="${1:-main}"
CURRENT="$(git rev-parse --abbrev-ref HEAD)"

if [ "$CURRENT" = "HEAD" ]; then
    echo "Dang o trang thai detached HEAD - checkout mot nhanh truoc da." >&2
    exit 1
fi

if [ "$CURRENT" = "$BASE" ]; then
    # Rebase main len chinh no khong co y nghia, va neu main da lech thi day la luc
    # can pull chu khong phai viet lai lich su cua nhanh chung.
    echo "Dang dung tren '$BASE'. Dung 'git pull --ff-only' thay vi rebase." >&2
    exit 1
fi

if [ -n "$(git status --porcelain)" ]; then
    echo "Working tree con thay doi chua commit. Commit hoac 'git stash' truoc khi rebase." >&2
    git status --short >&2
    exit 1
fi

echo "==> Fetch origin"
git fetch origin --prune

echo "==> Rebase '$CURRENT' len 'origin/$BASE'"
if ! git rebase "origin/$BASE"; then
    cat >&2 <<'EOF'

Rebase dung lai vi xung dot.
  - Sua file bi xung dot, roi: git add <file> && git rebase --continue
  - Muon quay lai nguyen trang:  git rebase --abort
EOF
    exit 1
fi

AHEAD="$(git rev-list --count "origin/$BASE..HEAD")"
echo
echo "Xong. '$CURRENT' dang o tren 'origin/$BASE', hon $AHEAD commit."

if git rev-parse --verify --quiet "origin/$CURRENT" >/dev/null; then
    cat <<EOF

Nhanh nay da co tren remote va lich su vua bi viet lai. Day len bang:

    git push --force-with-lease

(--force-with-lease chu khong phai --force: neu co nguoi vua day them commit len
 '$CURRENT', lenh se tu choi thay vi xoa mat cong cua ho.)
EOF
fi
