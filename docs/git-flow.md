# Git flow — AioKin

Quy uoc nhanh, commit, rebase va merge cho repo nay.

## 1. Nhanh

| Nhanh | Vai tro | Ai ghi vao |
|---|---|---|
| `main` | Ma dang chay tren production. Luon deploy duoc. | Chi qua PR |
| `develop` | Nhanh tich hop, gom cac tinh nang cho ban phat hanh sau. | Chi qua PR |
| `feat/*` | Mot tinh nang. | Tac gia |
| `fix/*` | Sua bug thuong. | Tac gia |
| `hotfix/*` | Sua gap tren production, tach thang tu `main`. | Tac gia |
| `chore/*`, `docs/*`, `refactor/*`, `ci/*` | Viec khong doi hanh vi san pham. | Tac gia |

Ten nhanh: `<type>/<mo-ta-ngan-gach-ngang>`, vi du `feat/facebook-sso`, `fix/otp-cooldown-race`.

Nhanh song cang lau cang kho merge. Nham moi nhanh **duoi mot tuan**; lau hon thi tach nho ra.

## 2. Commit

Theo Conventional Commits:

```
<type>(<scope>): <mo ta ngan bang tieng Anh, imperative, khong cham cuoi>
```

- **type**: `feat` | `fix` | `docs` | `style` | `refactor` | `perf` | `test` | `chore` | `build` | `ci`
- **scope** goi y cho repo nay: `auth`, `admin`, `account`, `redis`, `db`, `email`, `ci`, `deps`
- Mo ta toi da ~72 ky tu.

```text
feat(auth): complete Google login via external cookie middleware
fix(db): make full_name generated expression immutable
docs(git-flow): add rebase and merge conventions
```

Than commit (khi thay doi khong tam thuong): sau dong trong, ghi **cai gi** va **vi sao**.
Breaking change: them doan bat dau bang `BREAKING CHANGE:` o footer.

Commit nhieu dong tren PowerShell:

```powershell
git commit -m "feat(auth): rotate refresh token on every refresh" -m @'
- Revoke truoc khi cap token moi de khong bao gio ton tai hai token cung song
- Doc lai role tu database thay vi tin role trong refresh token
'@
```

## 3. Rebase hay merge — quy tac

Quy tac chi co mot cau: **rebase khi cap nhat nhanh cua minh, merge khi dua nhanh vao dich.**

### 3.1 Rebase — dung khi keo `main` moi nhat vao nhanh dang lam

```bash
git switch feat/facebook-sso
git fetch origin
git rebase origin/main
```

Vi sao khong `git merge origin/main` vao nhanh cua minh: moi lan dong bo se de lai mot
commit merge trong nhanh tinh nang. Mot PR ba tuan tuoi se co nam commit merge xen giua
cac commit that, va lich su cua no khong con doc duoc. Rebase dat cong viec cua ban len
tren dinh `main` hien tai, nen diff cua PR dung bang thu ban thuc su thay doi.

### 3.2 Merge — dung khi dua nhanh vao `main` / `develop`

Qua PR tren GitHub. Ruleset cua `main` chi cho **Squash** va **Rebase**:

| Cach | Dung khi |
|---|---|
| **Squash and merge** | Mac dinh. Nhanh co nhieu commit "fix typo", "wip" — gop lai thanh mot commit sach tren `main`. |
| **Rebase and merge** | Nhanh da co san cac commit tach bach, moi commit deu build duoc va dang giu rieng. |
| **Create a merge commit** | Bi tat tren `main`. Chi mo tren `develop`, cho ban phat hanh gom nhieu tinh nang. |

### 3.3 Ranh gioi tuyet doi

> **Khong bao gio rebase mot nhanh da co nguoi khac dua vao lam viec.**

Rebase viet lai commit hash. Nguoi da `git pull` nhanh do se co hai ban sao cua cung mot
thay doi, va lan merge sau se sinh xung dot o nhung cho khong ai sua. Nhanh ca nhan chua
ai dung thi rebase thoai mai; nhanh dung chung thi merge.

`main` va `develop` duoc ruleset chan `non_fast_forward`, nen khong ai force-push duoc —
ke ca khi go nham lenh.

### 3.4 Xu ly xung dot khi rebase

```bash
git rebase origin/main
# ... sua file bi xung dot ...
git add <file>
git rebase --continue

# Roi vao mo hon, muon quay lai nguyen trang:
git rebase --abort
```

Sau khi rebase, nhanh cua ban da lech voi ban tren remote — day len phai dung:

```bash
git push --force-with-lease
```

Dung `--force-with-lease` chu **khong** phai `--force`: neu trong luc do co nguoi day
them commit len nhanh, `--force-with-lease` tu choi thay vi xoa mat cong cua ho.

## 4. Vong doi mot thay doi

```bash
# 1. Bat dau tu main moi nhat
git switch main
git pull --ff-only

# 2. Tach nhanh
git switch -c feat/facebook-sso

# 3. Lam viec, commit nho va co y nghia
git add -p
git commit -m "feat(auth): read Facebook profile from Graph API"

# 4. Truoc khi mo PR — dong bo bang rebase
git fetch origin
git rebase origin/main

# 5. Day len va mo PR
git push -u origin feat/facebook-sso
gh pr create --fill --base main

# 6. Sau khi duoc duyet: Squash and merge tren GitHub, roi don dep
git switch main
git pull --ff-only
git branch -d feat/facebook-sso
```

Buoc 4 va 6 co san script: xem [muc 6](#6-script-ho-tro).

## 5. Hotfix

```bash
git switch main
git pull --ff-only
git switch -c hotfix/otp-not-sent
# ... sua, commit ...
git push -u origin hotfix/otp-not-sent
gh pr create --fill --base main --label hotfix
```

Sau khi hotfix vao `main`, phai dua nguoc ve `develop`, neu khong ban phat hanh sau se
lam mat chinh ban va:

```bash
git switch develop
git pull --ff-only
git merge --no-ff main
git push
```

## 6. Script ho tro

| Lenh | Viec |
|---|---|
| `scripts/git-sync.sh` (hoac `.ps1`) | Fetch roi rebase nhanh hien tai len `origin/main`, kem kiem tra an toan. |
| `scripts/git-land.sh` (hoac `.ps1`) | Sau khi PR da merge: ve `main`, pull, xoa nhanh da xong o ca local lan remote. |

Ca hai deu tu choi chay khi working tree con thay doi chua commit, va khi dang dung tren
chinh `main` — hai truong hop de mat viec nhat.

## 7. Truoc khi mo PR

- `dotnet build AioKin.sln -c Release` — sach, khong warning moi
- Doi schema thi da co migration: `dotnet ef migrations add <Ten> --project AioKin/AioKin.csproj --output-dir Data/Migrations`
- Khong co secret trong diff. Cau hinh cuc bo dung `dotnet user-secrets` hoac bien moi truong,
  `appsettings.json` chi giu khoa voi gia tri rong.
