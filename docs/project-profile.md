# Project Profile — AioKin

> Reusable architecture & codebase map. Learned on 2026-09-27.
> Target Stack: .NET 9 (C# 13) Web API + PostgreSQL (EF Core) + Redis + xUnit.

---

## 1. Stack & Runtime
- **Framework:** .NET 9.0 (`Microsoft.NET.Sdk.Web`, `net9.0`)
- **Language:** C# 13 (Nullable reference types enabled, implicit usings, top-level statements in Program.cs)
- **Primary Database:** PostgreSQL 16+ via `Npgsql.EntityFrameworkCore.PostgreSQL` (9.0.4), `EFCore.NamingConventions` (9.0.0, snake_case convention)
- **Schemas:** `core`, `family`, `security`, `sync`, `vault`
- **Cache & Session:** StackExchange.Redis (2.9.32) / Upstash REST / MemoryCache fallback
- **Data Protection:** `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` (9.0.9) persisted to `security.data_protection_keys`
- **Email:** Brevo API v3 (`IEmailService`)
- **Storage:** Supabase Storage (`IBlobStorageService`)
- **Documentation:** Swagger / OpenAPI 3.0 via `Swashbuckle.AspNetCore` (7.2.0)

---

## 2. Architecture & Layering

```
[ HTTP Requests ]
       │
       ▼
Controllers (`AioKin/Controllers/*`)
       │
       ▼
Services (`AioKin/Services/*`) ───► Redis / Brevo / Supabase Storage
       │
       ▼
Data Layer (`AioKinDbContext` + Entities in `AioKin/Data/*`)
       │
       ▼
PostgreSQL Database
```

### Response Pattern
- Controllers return uniform `OperationResult` or `OperationResult<T>`:
  - `OperationResult.Ok(data)` -> 200 OK
  - `OperationResult.Fail(code, message)` -> HTTP 400/401/403/404/422/409
  - Fluent extension: `result.ToActionResult()` maps result status to HTTP response.

### Authentication & Authorization
- **Access Tokens:** Opaque 32-byte cryptographically secure random tokens stored in Redis (`AccessTokenSession`), verified by `OpaqueAccessTokenAuthenticationHandler` (`Bearer <token>`).
- **Refresh Tokens:** Hashed via SHA-256 in Redis, carrying `device_id`, single-use rotation, automatic revocation.
- **Biometric Login:** ECDSA P-256 signature verification over server-generated nonces, public keys stored in `security.device_credentials`.
- **External OAuth:** Google & Facebook OAuth with transient cookie scheme `ExternalTempCookie`.
- **Data Protection:** Persistent keyring in PostgreSQL `security.data_protection_keys` (`PersistKeysToDbContext<AioKinDbContext>()`).

---

## 3. Key Domains & Schemas

| Domain | Schema | Entities | Key Responsibilities |
|---|---|---|---|
| **Security / Auth** | `security` | `User`, `Staff`, `Role`, `DeviceCredential`, `DataProtectionKey` | Identity, credentials, sessions, biometric keys, RBAC |
| **Family** | `family` | `Family`, `FamilyMember`, `FamilyInvite` | Family management, invite codes, role hierarchy |
| **Vault** | `vault` | `Space`, `SpaceMember`, `Category`, `Tag`, `Prompt`, `PromptVariable`, `PromptTag` | Personal & team prompt spaces, prompt CRUD, search |
| **Sync** | `sync` | `Device`, `SyncLogEntry`, `SyncConflict`, `BackupSnapshot` | Offline-first sync engine: push, pull, conflict detection |
| **Core** | `core` | `DiscoveryItem`, `ScheduleItem`, `Location` | General content and discovery feed |

---

## 4. Catalog of Reusable Services & Components

### Auth Services (`AioKin/Services/Auth/`)
- `IAccessTokenService` (`AccessTokenService.cs`): Issues, validates, revokes opaque tokens in Redis.
- `IRefreshTokenService` (`RefreshTokenService.cs`): Rotates, revokes refresh tokens per device.
- `IBiometricAuthService` (`BiometricAuthService.cs`): Nonce challenge & ECDSA P-256 verification.
- `IOAuthService` (`OAuthService.cs`): Google & Facebook social logins.
- `IUserService` (`UserService.cs`): User profile, registration, password hashing.

### Vault & Sync Services (`AioKin/Services/Vault/`)
- `ISpaceService` (`SpaceService.cs`): Space membership, personal/team space resolution.
- `IPromptBrowseService` (`PromptBrowseService.cs`): Prompt pagination, category/tag filtering.
- `ISyncService` (`SyncService.cs`): Push batch processing, pull with cursor, conflict detection.
- `ISpaceContext` (`SpaceContext.cs`): Contextual access control per Space.

### Common Utilities (`AioKin/Common/`)
- `OperationResult.cs`: Standardized service/API return type.
- `ActionResultExtensions.cs`: Converts `OperationResult` to HTTP status codes.
- `PasswordHasher.cs`: PBKDF2 with SHA-512 password hashing.
- `TokenHash.cs`: SHA-256 token hashing for Redis key storage.

---

## 5. Development & Testing Conventions
- **Migrations:** `dotnet ef migrations add <Name> --project AioKin/AioKin.csproj --output-dir Data/Migrations`
- **Raw SQL Mirror:** Keep `db/init-postgres.sql` in sync with migrations.
- **Testing:** `dotnet test -c Release`. Each test run spins up a dedicated PostgreSQL database per fixture via `ApiFixture.cs`.
- **Code Intelligence:** GitNexus index covers symbols and execution flows (`node .gitnexus/run.cjs`). Always run impact analysis before altering core services (`SyncService`, `AioKinDbContext`, etc.).
