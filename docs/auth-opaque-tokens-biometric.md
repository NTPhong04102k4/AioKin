# Spec — Opaque Access/Refresh Tokens, Session Management, Biometric Login (Cach B)

> Tai lieu thiet ke. Ba plan thuc thi doc tai:
> `docs/superpowers/plans/2026-09-25-opaque-access-tokens.md`,
> `docs/superpowers/plans/2026-09-25-token-session-management.md`,
> `docs/superpowers/plans/2026-09-25-biometric-device-login.md`.

## 1. Muc tieu

Ba quyet dinh da chot trong hoi thoai truoc tai lieu nay:

1. **Bo JWT cho access token, dung opaque token cho ca access lan refresh.** Ly do: thu hoi
   tuc thi (xoa 1 key Redis) thay vi phai duy tri blacklist song song theo TTL nhu JWT.
2. **Quan ly token/phien theo thiet bi** — nguoi dung xem duoc minh dang dang nhap o dau,
   dang xuat tu xa duoc 1 thiet bi cu the.
3. **Dang nhap sinh trac hoc theo Cach B** (challenge/response, keypair rang vao thiet bi,
   kieu FIDO2-lite) thay vi chi dung sinh trac de mo khoa storage cuc bo.

**Rang buoc chot, ap dung cho ca 3 phan tren:** toan bo auth la **.NET thuan**.
Supabase (khi dung) chi la noi host Postgres/Storage — **khong dung Supabase Auth**,
khong dung RLS/`auth.uid()` de phan quyen. Access token, refresh token, OTP, OAuth,
biometric — tat ca ky/luu/verify trong `AioKin/Services/Auth/**`, tu quan ly hoan toan
trong backend .NET. Day la quyet dinh kien truc da chot tu truoc (`docs/database.md`),
khong phai chuyen mo lai o tung tinh nang.

## 2. Hien trang (truoc khi doi)

| Thanh phan | File | Co che |
|---|---|---|
| Access token | `AioKin/Services/Auth/Token/JwtTokenService.cs` | JWT HS256, tu chua claim, verify bang chu ky (`AddJwtBearer` trong `Program.cs`) |
| Thu hoi access token | `AioKin/Middleware/JwtBlacklistMiddleware.cs` | Blacklist theo `jti` trong Redis, TTL = thoi gian con lai cua token |
| Refresh token | `AioKin/Services/Auth/RefreshToken/RefreshTokenService.cs` | **Da opaque tu truoc** — chuoi random 64 byte, luu RAW token lam Redis key (`auth:refresh:{token}` → `"{userCode}\|{role}"`), TTL 7 ngay |
| Danh sach phien theo user | `RedisKeys.UserRefreshTokens` | Chuoi token noi bang `\n`, gioi han 10 token/user, day token cu ra khoi danh sach thi thu hoi luon |
| Danh tinh trong request | `AioKin/Common/AioKinClaims.cs` | Doc tu `ClaimsPrincipal` (`GetJti`, `GetUserCode`, `GetUsername`, `GetUserUuid`, `GetStaffId`, `GetRole`) |

**Nguoi goi `IJwtTokenService` / `IRefreshTokenService` hien tai** (xac dinh bang
`impact()`/grep, khong doan): `AuthController`, `AdminAuthService`, `OAuthService`,
`Program.cs` (DI), va rieng `RevokeAllAsync` con duoc goi tu `AccountController`,
`UserManagementController`, `StaffManagementService` (2 cho). Day la toan bo blast radius
can dung khi doi hai service nay.

## 3. Kien truc muc tieu — Phan A: Opaque Access Token

- `IJwtTokenService`/`JwtTokenService` → thay bang `IAccessTokenService`/`AccessTokenService`
  (cung thu muc `AioKin/Services/Auth/Token/`).
- Access token la chuoi random opaque (giong cach `RefreshTokenService` dang sinh), luu
  session data trong Redis: key = `auth:session:{sha256(token)}` (bam truoc khi dung lam
  key — token goc **khong** nam trong Redis, khac diem yeu hien tai cua refresh token dang
  luu RAW token lam key). TTL = `Jwt:ExpiryMinutes` (giu nguyen ten config de khong phai doi
  bien moi truong da trien khai).
- Gia tri luu la JSON `AccessTokenSession` (Kind, Subject, UserUuid?, StaffId?, Username,
  Email?, Name, Role, DeviceId?, DeviceName?, Platform?, IssuedAtUnix) — du de dung lai
  `ClaimsPrincipal` giong het claim JWT cu, khong mat thong tin nao.
- `OpaqueAccessTokenAuthenticationHandler` (thay `AddJwtBearer`) doc header
  `Authorization: Bearer <token>`, tra Redis, dung `ClaimsPrincipal` thu cong. Khong con
  chu ky de verify — "hop le" = "con ton tai va chua het TTL trong Redis".
- `JwtBlacklistMiddleware` **bi xoa hoan toan**: thu hoi gio la xoa key Redis, co hieu luc
  ngay tai buoc authenticate, khong can tang chan rieng nua.
- `RefreshTokenService` doi sang bam key (cung ly do bao mat) va gan `DeviceId` vao payload
  (JSON thay vi chuoi `"{userCode}\|{role}"`) — can cho Phan B.
- Dead code duoc don: `JwtConfiguration.ResolveIssuer/ResolveAudiences/ResolveAudienceForSigning`
  (chi ResolveAccessTokenMinutes con dung), `Jwt:Key`/`Jwt:Issuer`/`Jwt:Audience` khong con
  y nghia (khong xoa khoi `appsettings*`/tai lieu deploy ngay — chi ngung doc, tranh phai
  sua cau hinh server truoc khi code moi len).

## 4. Kien truc muc tieu — Phan B: Quan ly token/phien

- Moi lan dang nhap/lam moi token, client gui kem `deviceId` (bat buoc sinh phia client,
  vd UUID luu trong Keystore/Keychain), `deviceName`, `platform` (optional, chi de hien thi).
  Thieu `deviceId` van chay duoc (app cu chua cap nhat) — server tu sinh id ngau nhien, phien
  do hien la "Unknown device" trong danh sach.
- `GET /account/sessions` — liet ke access session dang song cua chinh user gui (id = 12 ky
  tu dau cua session hash — khong the dao nguoc ra token that, an toan de lo cho chinh chu),
  kem `deviceName`, `platform`, `issuedAt`, `isCurrent`.
- `DELETE /account/sessions/{id}` — thu hoi dung access session do **va** moi refresh token
  cung `deviceId` (khong lam vay thi refresh token cua thiet bi bi "logout" van song va tu
  cap access token moi vai phut sau — vo hieu hoa tinh nang).
- `logout-all` hien co khong doi hanh vi (van thu hoi tat ca), chi noi bo dung lai cung engine.

## 5. Kien truc muc tieu — Phan C: Biometric (Cach B)

Server **khong bao gio** nhan du lieu sinh trac hoc. Luong:

1. Sau khi dang nhap thuong (password/OTP/OAuth) it nhat 1 lan, thiet bi tao **keypair
   ECDSA P-256** trong Android Keystore (`setUserAuthenticationRequired(true)`) / iOS Secure
   Enclave. Private key khong bao gio roi khoi phan cung, va moi lan dung deu doi sinh trac.
2. `POST /auth/biometric/register` *(can Bearer token thuong)* — client gui `deviceId` +
   public key (SPKI, base64) → server luu `security.device_credentials`. Goi lai voi cung
   `deviceId` (doi key, cai lai app) thi ghi de ban ghi cu.
3. `POST /auth/biometric/challenge` — body `{userCode, deviceId}`. Server **luon** tra ve
   `challengeId` + `nonce` moi bat ke `(userCode, deviceId)` co credential hay khong — khong
   phan biet "khong ton tai" voi "co ton tai" o buoc nay, cung nguyen tac voi `/auth/login`.
   `{challengeId → userCode, deviceId, nonce}` luu tam trong Redis, TTL 2 phut.
4. `POST /auth/biometric/verify` — body `{challengeId, signature}`. Client mo private key
   bang sinh trac de ky `nonce` (khong phai server gui lai nonce o buoc nay — client da giu
   tu response cua buoc 3). Server xoa `challengeId` khoi Redis **truoc** khi verify (dung
   mot lan, chan replay/nhieu lan thu song song), tra cuu `device_credentials` theo
   `(userCode, deviceId)` da luu trong challenge, verify chu ky ECDSA bang public key luu san.
   Sai o buoc nao (challenge het han, khong co credential, chu ky sai) deu tra chung
   `InvalidCredentials` — khong lo thong tin nao ve viec thiet bi/tai khoan co dang ky
   sinh trac hay khong. Thanh cong thi phat cap access+refresh token opaque moi, y het
   luong dang nhap thuong (`IAccessTokenService`/`IRefreshTokenService`, cung gan `deviceId`).
5. `DELETE /auth/biometric/{deviceId}` *(can Bearer token thuong)* — set `revoked_at` cho
   credential cua chinh user, khong xoa dong (giu vet).

Bang moi — schema `security` (giu chung schema voi `users`, dung quy uoc dat ten hien co):

```
security.device_credentials
    device_credential_id  uuid PK
    user_id                uuid FK -> security.users(user_id) ON DELETE CASCADE
    device_id              varchar(100)   -- id client tu sinh, trung voi deviceId cua session/refresh token
    device_name            varchar(120) NULL
    platform               varchar(20)  NULL
    public_key             text            -- SPKI, base64
    created_date           timestamptz
    last_used_date         timestamptz NULL
    revoked_at             timestamptz NULL
    UNIQUE (user_id, device_id)
```

Revoke 1 thiet bi (vd nguoi dung bam "xoa dang nhap sinh trac" trong app, hoac admin nghi
ngo lo thiet bi) = set `revoked_at`, khong xoa cung — giu vet.

## 6. Bang API thay doi/them moi

| Method | Duong dan | Thay doi |
|---|---|---|
| POST | `/auth/login` | Body them `deviceId?`, `deviceName?`, `platform?` |
| POST | `/auth/verify-otp` | Tuong tu |
| POST | `/auth/refresh-token` | Tuong tu — `deviceId` cua lan refresh phai la thiet bi da phat refresh token do (khong bat buoc kiem tra trung khop trong M0 cua tinh nang nay — ghi nhan de lam sau neu can chat) |
| GET | `/account/sessions` | **Moi.** Danh sach thiet bi dang dang nhap |
| DELETE | `/account/sessions/{id}` | **Moi.** Dang xuat 1 thiet bi |
| POST | `/auth/biometric/register` | **Moi.** Dang ky public key sau khi da dang nhap thuong |
| POST | `/auth/biometric/challenge` | **Moi.** Xin nonce de dang nhap bang sinh trac |
| POST | `/auth/biometric/verify` | **Moi.** Xac thuc chu ky, phat token nhu dang nhap thuong |
| DELETE | `/auth/biometric/{deviceId}` | **Moi.** Thu hoi 1 credential sinh trac (mat may, doi thiet bi) — set `revoked_at`, khong xoa dong |

Khong endpoint nao trong so nay boc ngoai `OperationResult` — chi `/posts`, `/todos`,
`/ability/rules` giu ngoai le do da chot tu truoc.

## 7. Quyet dinh chot (khong tranh luan lai o tung plan)

| Van de | Quyet dinh | Vi sao |
|---|---|---|
| Thuat toan chu ky sinh trac | ECDSA P-256 (`SHA256withECDSA`) | Duoc ca Android Keystore lan iOS Secure Enclave ho tro native, khong can thu vien ngoai |
| Luu public key | Postgres (`security.device_credentials`), khong phai Redis | Day la du lieu lau dai, khong phai session — mat Redis khong duoc lam mat kha nang dang nhap sinh trac |
| Nonce challenge | Redis, TTL 2 phut, dung 1 lan (xoa ngay sau verify) | Chan replay; 2 phut du cho thao tac sinh trac tren thiet bi cham nhat |
| Dinh dang chu ky ECDSA | DER (`DSASignatureFormat.Rfc3279DerSequence` o .NET) | Android `Signature.getInstance("SHA256withECDSA")` va iOS `SecKeyCreateSignature` deu xuat DER mac dinh — dung IEEE P1363 (mac dinh cua .NET) se verify that voi chu ky hop le |
| Định danh session cong khai | 12 ky tu dau cua `sha256(token)` (hex) | Khong dao nguoc duoc ve token that, du duy nhat de hien thi/chon xoa |
| Revoke 1 thiet bi | Xoa access session + moi refresh token cung `deviceId` | Thieu ve refresh se khien "logout" khong that su co hieu luc |
| `Jwt:*` config keys | Giu ten `Jwt:ExpiryMinutes`, ngung doc `Jwt:Key/Issuer/Audience` | Khong bat doi bien moi truong da trien khai tren server |
| Thu tu trien khai | A (opaque token) → B (quan ly phien) → C (biometric) | C phu thuoc A (can access/refresh opaque de phat token cuoi luong) va B (can `deviceId` tren refresh token de tag credential) |

## 8. Ngoai pham vi (khong lam trong 3 plan nay)

- Doi ten/xoa `Jwt:Key` khoi tai lieu deploy that (Render/Supabase) — chi ngung doc trong
  code, dong bo tai lieu la viec rieng sau khi code da chay on dinh.
- Thay doi client Android/iOS that (repo `NativeKotlin`/`IOSAPP`) — 3 plan nay chi lam phan
  backend AioKin; phia client phai tu cap `deviceId` on dinh, tao keypair, va goi 3 endpoint
  biometric — la mot plan rieng o repo tuong ung.
- Doi soat/canh bao khi 1 tai khoan dang nhap tu qua nhieu thiet bi/dia diem bat thuong —
  ngoai pham vi bao mat co ban nay.
