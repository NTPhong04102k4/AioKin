# Database — AioKin

Luong khoi tao va migrate Postgres (Supabase) cho repo nay. Doc file nay truoc khi dung
`dotnet ef` hoac dung `db/init-postgres.sql`.

## 1. Kien truc

- ORM: EF Core 9 + Npgsql, DbContext o `AioKin/Data/AioKinDbContext.cs`.
- DB: Postgres do Supabase quan ly, 3 schema logic: `core` (locations, discovery_items,
  schedule_items), `security` (roles, users, staff), `family` (families, family_members,
  family_invites).
- Nguon su that ve schema la thu muc `AioKin/Data/Migrations/` — KHONG phai class model.
  Model C# co the di truoc migration (da tung xay ra, xem muc 4); table thuc te tren DB
  chi duoc tao khi co migration tuong ung.

## 2. Hai con duong tao/cap nhat schema

### 2.1. Khoi tao lan dau tren DB trong (khuyen dung cho Supabase)

DB Supabase moi tao khong co bang nao. Thay vi de app tu `MigrateAsync()` luc start (cham,
de timeout qua pooler — xem muc 5), chay thang file SQL da sinh san tu migration:

```
db/init-postgres.sql
```

File nay sinh boi:

```bash
cd AioKin
dotnet ef migrations script -o ../db/init-postgres.sql
```

Chay 1 lan tren DB trong, qua Supabase SQL Editor hoac psql:

```bash
psql "host=<host> port=<port> dbname=postgres user=<user> sslmode=require" -f db/init-postgres.sql
```

Script nay:
1. Tao 3 schema (`core`, `security`, `family`) va toan bo bang/index/FK.
2. Tu ghi 3 dong vao `__EFMigrationsHistory` — danh dau cac migration hien co
   (`InitialCreate`, `AddDiscoveryAndScheduleItems`, `AddFamily`) la "da chay".
3. Boc trong `START TRANSACTION ... COMMIT` — loi giua chung thi rollback toan bo,
   khong de DB o trang thai dang do.

**Luu y:** ban khong dung `--idempotent`, nen script nay chi chay dung 1 lan tren DB
trong. Chay lai lan 2 se loi "already exists" (dung y, vi day la script khoi tao, khong
phai script ap dung lai duoc).

### 2.2. Thay doi schema ve sau (them cot, bang, sua kieu du lieu...)

Sau khi DB da co du lieu that, khong dung lai `init-postgres.sql` nua. Di theo migration
chuan cua EF Core:

```bash
cd AioKin
# 1. Sua model (DbSet, entity, Fluent API trong AioKinDbContext.OnModelCreating)
# 2. Sinh migration moi
dotnet ef migrations add <TenMoTaThayDoi>

# 3. Xem truoc SQL se chay (khuyen dung truoc khi dung len Supabase that)
dotnet ef migrations script <MigrationTruoc> <MigrationMoi> -o preview.sql

# 4. Ap dung
dotnet ef database update
```

Nguyen tac: moi migration nen backward-compatible voi ban da co du lieu — them cot moi
lam nullable hoac co default, khong doi kieu cot dang co data ma khong co buoc trung gian.

## 3. Migrate luc app khoi dong — chi con opt-in

`Program.cs` (quanh dong 362-382) tach rieng buoc migrate khoi vong doi khoi dong:

| Moi truong | Hanh vi mac dinh |
|---|---|
| `Development` (`dotnet run` local) | Tu dong `MigrateAsync()` + seed, nhu truoc gio |
| Khac (staging/production, vd Render) | **Bo qua**, tru khi bien moi truong `RUN_MIGRATIONS=true` duoc set tuong minh |

Ly do tach: nhieu instance cung migrate mot luc gay tranh chap; 1 migration loi khong
duoc keo sap ca app dang chay on dinh; can co buoc review SQL truoc khi no chay that tren
prod. Chi tiet xem chu thich tai `Program.cs`.

Quy trinh deploy khuyen dung: chay migration nhu mot buoc rieng (CI job hoac
`dotnet ef database update` tu may ban) TRUOC khi deploy phien ban app moi, KHONG de app
tu migrate luc start.

## 4. Bay da gap phai — ghi lai de khoi lap lai

- **appsettings.Development.json ghi de appsettings.json**: connection string that
  (Supabase) phai nam trong `appsettings.Development.json` (hoac bien moi truong
  `ConnectionStrings__DefaultConnection`) de co tac dung khi chay launch profile
  `http`/`https` (`ASPNETCORE_ENVIRONMENT=Development`). Sua `appsettings.json` (file
  goc) khong du.
- **Bien moi truong de cao hon file json**: neu ban set
  `$env:ConnectionStrings__DefaultConnection` trong shell, no se de len moi gia tri trong
  appsettings.*.json cho toi khi shell dong hoac bien duoc xoa.
- **Supabase Direct Connection (`db.<ref>.supabase.co:5432`) chi co ban ghi DNS IPv6**.
  Mang khong co IPv6 se gap loi `SocketException 11004`. Dung Transaction Pooler
  (`<region>.pooler.supabase.com:6543`, username dang `postgres.<ref>`) thay the.
- **Transaction Pooler cham voi DDL**: lenh `CREATE TABLE` qua pooler co the mat >30s do
  do tre mang, vuot `CommandTimeout` mac dinh cua Npgsql. Neu can migrate qua ket noi nay,
  tang timeout trong connection string: `Timeout=60;Command Timeout=120`. On dinh hon la
  chay migration/SQL mot lan thu cong (muc 2.1/2.2) thay vi de app tu migrate qua pooler
  moi lan start.
- **`dotnet ef migrations remove --no-build` dung DLL cu**: neu vua `migrations add` va
  build lai chua kip, `remove` co the xoa nham migration cu thay vi migration vua tao. Bo
  `--no-build` (hoac build lai truoc) khi can `migrations remove` ngay sau `migrations add`.

## 5. Secrets — dang o dau, dang track hay khong

- `appsettings.json` (goc, **co track trong git**) va `appsettings.Development.json` (goc,
  **co track trong git**) khong duoc chua secret that (password DB, JWT key, API key...).
  Bat ky gia tri that nao dan vao 2 file nay deu co nguy co bi commit.
- Quy uoc gitignore san co cho secret cuc bo: `appsettings.Local.json`,
  `appsettings.*.local.json`, `.env`, `.env.*` (tru `.env.example`). **Luu y:** cac file
  `.local.json` KHONG tu dong duoc ASP.NET Core load — Program.cs hien chua co
  `AddJsonFile` cho pattern nay. Muon dung duoc can hoac (a) tu them
  `builder.Configuration.AddJsonFile("appsettings.{Environment}.local.json", optional: true)`
  vao Program.cs, hoac (b) dung `dotnet user-secrets` (can `UserSecretsId` trong
  `AioKin.csproj`, hien chua co), hoac (c) dung bien moi truong luc chay.
