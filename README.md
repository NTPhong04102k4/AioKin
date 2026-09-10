# AioKin API

Backend ASP.NET Core 9 cho AioKin: xac thuc nguoi dung, quan ly tai khoan va quan ly nhan vien.

**Stack:** .NET 9 · EF Core 9 + PostgreSQL · Redis (StackExchange / Upstash REST / MemoryCache) ·
JWT Bearer · OAuth2 Google & Facebook · Brevo (email giao dich)

---

## Chay tren may

```bash
# 1. Postgres + Redis (Redis khong bat buoc o Development)
docker run -d --name aiokin-pg  -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16-alpine
docker run -d --name aiokin-rd  -p 6379:6379 redis:7-alpine

# 2. Tai khoan quan tri dau tien (khong co thi khong ai dang nhap duoc phia admin)
cd AioKin
dotnet user-secrets set "Auth:SuperAdmin:Username" "superadmin"
dotnet user-secrets set "Auth:SuperAdmin:Email"    "admin@aiokin.local"
dotnet user-secrets set "Auth:SuperAdmin:Password" "<mat-khau-manh>"

# 3. Chay — migration va seed tu dong chay luc khoi dong
dotnet run
```

Swagger: `http://localhost:5xxx/swagger` (chi bat o Development).

Chua co Redis va chua co API key Brevo thi app van chay: Redis roi ve `IMemoryCache`, con
email duoc **ghi ra log** thay vi gui — ma OTP hien ngay trong console. Ca hai duong roi nay
bi chan o Production; app se dung khoi dong thay vi chay sai lang le.

## Cau hinh

`appsettings.json` chi giu khoa voi gia tri rong. Gia tri that dat qua `dotnet user-secrets`
(may dev) hoac bien moi truong (server, dung `__` thay cho `:`).

| Khoa | Bat buoc | Ghi chu |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | ✅ | Cung chap nhan `DATABASE_URL` dang `postgres://…` |
| `Jwt:Key` | ✅ | Toi thieu 32 byte; app tu choi khoi dong neu ngan hon |
| `Jwt:Issuer`, `Jwt:Audience` | — | `Audience` nhan mot chuoi hoac mang |
| `Jwt:ExpiryMinutes` | — | Mac dinh 60 |
| `Jwt:RefreshTokenExpiryDays` | — | Mac dinh 7 |
| `Redis:ConnectionString` | Prod ✅ | `localhost:6379` hoac `rediss://user:pass@host` |
| `UPSTASH_REDIS_REST_URL` / `_TOKEN` | — | Dung khi host chan TCP ra ngoai |
| `Email:ApiKey`, `Email:SenderEmail` | Prod ✅ | Brevo transactional |
| `Authentication:Google:ClientId` / `ClientSecret` | — | Thieu thi tat dang nhap Google |
| `Authentication:Facebook:AppId` / `AppSecret` | — | Thieu thi tat dang nhap Facebook |
| `Cors:AllowedOrigins` | — | Mang. Rong = cho phep moi origin, khong kem cookie |
| `Frontend:Origin` | — | Dich cua `postMessage` sau khi SSO xong |
| `Auth:SuperAdminRecoveryCode` | — | Rong = **tat** endpoint khoi phuc SuperAdmin |
| `Auth:SuperAdmin:*` | — | Seed tai khoan quan tri dau tien |

Redirect URI can khai bao ben nha cung cap: `/auth/callback/google`, `/auth/callback/facebook`.

## Endpoint

### `/auth` — khach hang

| Method | Duong dan | Ghi chu |
|---|---|---|
| POST | `/auth/register` | Gui OTP; chua tao user cho toi khi xac thuc |
| POST | `/auth/verify-otp` | Tao user va tra ve token luon |
| POST | `/auth/resend-otp` | Cooldown 60 giay moi email |
| POST | `/auth/login` | Username / email / so dien thoai |
| POST | `/auth/refresh-token` | Xoay vong: token cu bi thu hoi ngay |
| POST | `/auth/logout` | Blacklist JTI trong Redis |
| POST | `/auth/logout-all` | Thu hoi moi refresh token cua tai khoan |
| POST | `/auth/forgot-password` | Buoc 1 — gui OTP |
| POST | `/auth/forgot-password/verify-otp` | Buoc 2 — gui mat khau tam qua email |
| POST | `/auth/reset-password` | Buoc 3 — dat mat khau moi |
| GET | `/auth/login/google` · `/auth/login/facebook` | Bat dau SSO |
| GET | `/auth/finalize/google` · `/auth/finalize/facebook` | Popup `postMessage` ve frontend |

### `/account` — tai khoan cua chinh minh (`Customer`)

`GET /account/me` · `PATCH /account/me` · `POST /account/me/change-password` · `POST /account/me/contact`

Danh tinh luon lay tu token, khong bao gio tu tham so.

### `/auth/admin` — xac thuc quan tri

`POST /auth/admin/login` · `POST /auth/admin/staff` *(SuperAdmin)* ·
`PUT /auth/admin/staff/{id}/password` · `POST /auth/admin/superadmin/recover-password`

### `/admin` — quan ly

| Nhom | Duong dan | Quyen |
|---|---|---|
| Nguoi dung | `GET /admin/users`, `…/recent/7-days`, `…/recent/30-days`, `GET /admin/users/{uuid}` | Staff, Admin, SuperAdmin |
| Nguoi dung | `PATCH /admin/users/{uuid}/lock`, `PATCH /admin/users/{uuid}/status` | Admin, SuperAdmin |
| Nhan vien | `GET /admin/staff`, `GET /admin/staff/{id}` | Admin, SuperAdmin |
| Nhan vien | `PUT`, `PATCH …/status`, `DELETE` | SuperAdmin |

## Ghi chu thiet ke

- **Mot khuon phan hoi loi.** Moi loi nghiep vu tra `OperationResult` voi `errorCode` on dinh;
  HTTP status suy ra tu `errorCode` o mot cho duy nhat (`OperationResultHttpExtensions`).
- **Ba tang Redis.** Ket noi duoc kiem tra that (`PING`) luc khoi dong, khong chi doc cau hinh.
- **Thu hoi token co hieu luc ngay.** Access token bi blacklist theo `jti`; refresh token la
  chuoi opaque trong Redis, nen doi mat khau / khoa tai khoan duoi duoc phien dang mo.
- **Khoa tai khoan.** Sai mat khau 5 lan → khoa 5 phut; dat lai mat khau se mo khoa.
- **Mot SuperAdmin duy nhat.** Rang buoc duoc giu ca luc tao lan luc doi role, va khong the
  vo hieu hoa tai khoan SuperAdmin cuoi cung.
- **Khong co ma khoi phuc mac dinh.** `Auth:SuperAdminRecoveryCode` rong thi endpoint khoi phuc
  tra 403 — mot ma hardcode trong repo la cua hau vao quyen cao nhat.

## Migration

```bash
dotnet ef migrations add <Ten> --project AioKin/AioKin.csproj --output-dir Data/Migrations
dotnet ef database update --project AioKin/AioKin.csproj
```

## Dong gop

Quy uoc nhanh, commit, rebase va merge: [`docs/git-flow.md`](docs/git-flow.md).
