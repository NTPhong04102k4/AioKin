# Biometric Device Login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a customer log in with device biometrics (Face ID / fingerprint) without the server ever seeing biometric data — the device signs a server-issued challenge with a private key that never leaves its Secure Enclave/Keystore, and the server verifies the signature against a public key registered earlier.

**Architecture:** Three endpoints under `auth/biometric`. `register` (requires an ordinary Bearer session) stores the device's ECDSA P-256 public key in a new `security.device_credentials` table, keyed by `(UserID, DeviceId)`. `challenge` looks up nothing yet — it always returns a fresh `challengeId` + random nonce and stashes `{userCode, deviceId, nonce}` in Redis for 2 minutes, so an anonymous caller can never learn whether a given user/device pair has biometric registered. `verify` deletes that Redis entry immediately (single use), then — and only then — looks up the credential and checks the signature; any failure at any step returns the same generic error. A successful verify issues an access+refresh token pair through the exact same `IAccessTokenService`/`IRefreshTokenService` this feature depends on, so a biometric login is indistinguishable from a password login from that point on. A fourth endpoint, `DELETE auth/biometric/{deviceId}`, lets the customer revoke a device's biometric credential (lost phone, reinstalled app).

**Tech Stack:** .NET 9, EF Core 9 + Npgsql, `System.Security.Cryptography.ECDsa` (P-256, DER signatures), `IRedisService`, xUnit + `ApiFixture` (real Postgres) for integration tests, plain xUnit for the pure-crypto verification helper.

**Spec:** `docs/auth-opaque-tokens-biometric.md` — section 5 (Phase C target, the exact wire sequence), section 6 (API table), section 7 (locked decisions: ECDSA P-256, DER signature format, Postgres for the credential, Redis+2min TTL for the nonce).

## Global Constraints

- **Comments and docs are Vietnamese without diacritics** ("khong dau").
- **This plan starts only after `docs/superpowers/plans/2026-09-25-token-session-management.md` is merged** — `VerifyAsync` issues tokens via `IAccessTokenService.CreateForCustomerAsync(user, DeviceInfo)` and `IRefreshTokenService.GenerateAsync(subject, role, deviceId)` exactly as that plan leaves them.
- **Biometric login is Customer-only.** `DeviceCredential.UserID` references `security.users`, never `security.staff` — staff sign in through a web admin dashboard with no biometric hardware to speak of.
- **`DeviceCredential` intentionally has no `SubjectType`/CASL rule**, unlike `Family`/`DiscoveryItem`/`ScheduleItem`. It is never exposed through a generic ability-checked CRUD surface — every operation on it is scoped to "the caller's own row" the same way `AccountController` already works without going through `IPermissionService`. Do not add a `SubjectType` constant for it; doing so would imply a rule needs seeding in `DbSeeder.SeedRolePermissionsAsync` that nothing will ever check.
- **Never log a public key, nonce, or signature at `Information` level or above** — they are not secrets in the cryptographic sense (a public key is public; a used-once nonce is worthless after verify), but logging them by habit invites someone to later add a real secret to the same log line. Log `deviceId`/`userCode`/pass-fail only.
- **Every failure branch in `verify` returns the same `OperationResult.Fail("InvalidCredentials", ...)`** — expired challenge, no such credential, revoked credential, and bad signature must be indistinguishable to the caller.
- **Run `detect_changes()` before the final commit.**

## Review Focus

- **`challenge` for a `(userCode, deviceId)` pair with no registered credential** must still return 200 with a nonce, identical in shape to a real one — this is the whole point of not checking existence at that step.
- **Reusing a `challengeId` a second time** (replay, or two concurrent `verify` calls racing on the same challenge) must fail the second attempt — the Redis entry is gone after the first `GetAsync`+`DeleteAsync`.
- **A revoked credential (`revoked_at` set) must fail verify** exactly like a missing one, not with a different error code.
- **A signature produced with the wrong nonce, or by a different keypair than the one registered**, must fail — a test must actually generate two different ECDSA keypairs and confirm cross-verification fails, not just test the happy path.
- **`register` called twice for the same `(userId, deviceId)`** (key rotation, app reinstall) must overwrite the stored public key rather than throwing a unique-constraint violation.

---

## Prerequisites

Branch from the finished token-session-management branch (per `docs/git-flow.md`):

```bash
git checkout feat/token-session-management
git pull
git checkout -b feat/biometric-device-login
```

---

## File Structure

**New:**

| File | Responsibility |
|---|---|
| `AioKin/Data/Entities/Security/DeviceCredential.cs` | The stored public key + device metadata, per `(UserID, DeviceId)` |
| `AioKin/Data/Migrations/<timestamp>_AddDeviceCredentials.cs` | EF migration (generated, see Task 1) |
| `AioKin/Services/Auth/Biometric/BiometricSignature.cs` | Pure ECDSA P-256/DER verify helper — no I/O |
| `AioKin/Services/Auth/Biometric/IBiometricAuthService.cs` | Contract: register, challenge, verify |
| `AioKin/Services/Auth/Biometric/BiometricAuthService.cs` | Implementation |
| `AioKin/Services/Auth/Biometric/BiometricChallenge.cs` | Redis payload record for a pending challenge |
| `AioKin/Models/InputModel/Auth/Biometric/BiometricRequests.cs` | `RegisterBiometricRequest`, `BiometricChallengeRequest`, `BiometricVerifyRequest` |
| `AioKin/Models/ViewModel/Auth/Biometric/BiometricChallengeResponse.cs` | `{ challengeId, nonce }` |
| `AioKin/Controllers/Auth/BiometricController.cs` | The four endpoints |
| `AioKin.Tests/Auth/BiometricSignatureTests.cs` | Pure crypto unit tests |
| `AioKin.Tests/Auth/BiometricAuthTests.cs` | Integration tests through `ApiFixture` |

**Modified:**

| File | Change |
|---|---|
| `AioKin/Data/AioKinDbContext.cs` | Add `DbSet<DeviceCredential> DeviceCredentials`, `OnModelCreating` entry |
| `AioKin/Common/RedisKeys.cs` | Add `BiometricChallenge(string challengeId)` key + `RedisTtl.BiometricChallenge` |
| `AioKin/Program.cs` | DI registration for `IBiometricAuthService` |

---

### Task 1: `DeviceCredential` entity, DbContext wiring, migration

**Files:**
- Create: `AioKin/Data/Entities/Security/DeviceCredential.cs`
- Modify: `AioKin/Data/AioKinDbContext.cs`
- Create (via CLI, not hand-written): `AioKin/Data/Migrations/<timestamp>_AddDeviceCredentials.cs` + `.Designer.cs`, and an update to `AioKinDbContextModelSnapshot.cs`
- Test: `AioKin.Tests/Auth/DeviceCredentialSchemaTests.cs`

**Interfaces:**
- Produces: `DeviceCredential` entity (`DeviceCredentialID`, `UserID`, `DeviceId`, `DeviceName?`, `Platform?`, `PublicKey`, `CreatedDate`, `LastUsedDate?`, `RevokedAt?`), `AioKinDbContext.DeviceCredentials`.

- [ ] **Step 1: Write `DeviceCredential.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Security;

/// <summary>
/// Public key ECDSA P-256 cua mot thiet bi, dung de dang nhap bang sinh trac hoc (Face
/// ID/van tay o tang OS, khong bao gio cham toi server). Private key tuong ung nam trong
/// Secure Enclave/Keystore cua thiet bi, khong bao gio roi khoi phan cung.
///
/// KHONG co SubjectType/CASL: bang nay khong di qua CRUD chung nao — moi thao tac chi tren
/// dong cua chinh nguoi goi, giong AccountController.
/// </summary>
[Table("device_credentials", Schema = "security")]
public class DeviceCredential
{
    [Key]
    public Guid DeviceCredentialID { get; set; } = Guid.NewGuid();

    /// <summary>Chi tro toi security.users — bio dang nhap la tinh nang cua Customer, khong phai Staff.</summary>
    public Guid UserID { get; set; }

    /// <summary>Id client tu sinh va giu on dinh — trung voi deviceId dung cho access/refresh token.</summary>
    [MaxLength(100)]
    public required string DeviceId { get; set; }

    [MaxLength(120)]
    public string? DeviceName { get; set; }

    [MaxLength(20)]
    public string? Platform { get; set; }

    /// <summary>SubjectPublicKeyInfo (SPKI), ma hoa base64.</summary>
    public required string PublicKey { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedDate { get; set; }

    /// <summary>Khac null nghia la da bi thu hoi — giu dong lai de con vet, khong xoa.</summary>
    public DateTime? RevokedAt { get; set; }
}
```

- [ ] **Step 2: Wire into `AioKinDbContext.cs`**

Add the `DbSet`, next to the other Security sets:

```csharp
    public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();
```

Add inside `OnModelCreating`, after the `Staff` entity block:

```csharp
        modelBuilder.Entity<DeviceCredential>(entity =>
        {
            // Mot thiet bi mot credential moi user — dang ky lai (doi key, cai lai app)
            // phai la UPDATE, khong phai insert them dong.
            entity.HasIndex(c => new { c.UserID, c.DeviceId }).IsUnique();

            entity.HasIndex(c => c.UserID);

            entity.HasOne<AioKin.Data.Entities.Security.User>()
                .WithMany()
                .HasForeignKey(c => c.UserID)
                .OnDelete(DeleteBehavior.Cascade);
        });
```

- [ ] **Step 3: Generate the migration**

Run:
```bash
cd AioKin
dotnet ef migrations add AddDeviceCredentials --output-dir Data/Migrations
cd ..
```
Expected: creates `AioKin/Data/Migrations/<timestamp>_AddDeviceCredentials.cs` + `.Designer.cs`, updates `AioKinDbContextModelSnapshot.cs`. Open the generated migration and confirm it creates exactly `security.device_credentials` with the unique index on `(user_id, device_id)` and the cascade FK — if EF picked different defaults, fix the entity/fluent config in Step 2 and regenerate (`dotnet ef migrations remove --output-dir Data/Migrations` first, per the `--no-build` pitfall noted in `README.md`).

- [ ] **Step 4: Write the schema test**

```csharp
using AioKin.Data.Entities.Security;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Auth;

[Collection(ApiCollection.Name)]
public class DeviceCredentialSchemaTests
{
    private readonly ApiFixture _fixture;

    public DeviceCredentialSchemaTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Mot_user_khong_the_co_hai_credential_cung_deviceId()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.DeviceCredentials.Add(new DeviceCredential { UserID = user.UserID, DeviceId = "device-1", PublicKey = "key-a" });
        await db.SaveChangesAsync();

        db.DeviceCredentials.Add(new DeviceCredential { UserID = user.UserID, DeviceId = "device-1", PublicKey = "key-b" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~DeviceCredentialSchemaTests`
Expected: PASS (the migration from Step 3 runs automatically — `ApiFixture` migrates on every test database it creates).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(auth): add security.device_credentials for biometric login"
```

---

### Task 2: `BiometricSignature` — pure ECDSA verify helper

**Files:**
- Create: `AioKin/Services/Auth/Biometric/BiometricSignature.cs`
- Create: `AioKin.Tests/Auth/BiometricSignatureTests.cs`

**Interfaces:**
- Produces: `BiometricSignature.Verify(string publicKeyBase64, string nonceBase64, string signatureBase64) : bool` — never throws; any malformed input (bad base64, wrong key type, wrong curve) is a `false`, not an exception, because it sits directly on the anonymous `verify` endpoint's request path.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Security.Cryptography;
using AioKin.Services.Auth.Biometric;
using Xunit;

namespace AioKin.Tests.Auth;

public class BiometricSignatureTests
{
    private static (string PublicKeyBase64, ECDsa Key) NewKeyPair()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), key);
    }

    private static string Sign(ECDsa key, string nonceBase64)
    {
        var nonceBytes = Convert.FromBase64String(nonceBase64);
        var signature = key.SignData(nonceBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return Convert.ToBase64String(signature);
    }

    [Fact]
    public void Chu_ky_dung_key_dung_nonce_thi_hop_le()
    {
        var (publicKey, key) = NewKeyPair();
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var signature = Sign(key, nonce);

        Assert.True(BiometricSignature.Verify(publicKey, nonce, signature));
    }

    [Fact]
    public void Chu_ky_boi_key_khac_thi_khong_hop_le()
    {
        var (publicKey, _) = NewKeyPair();
        var (_, otherKey) = NewKeyPair();
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var signature = Sign(otherKey, nonce);

        Assert.False(BiometricSignature.Verify(publicKey, nonce, signature));
    }

    [Fact]
    public void Chu_ky_cho_nonce_khac_thi_khong_hop_le()
    {
        var (publicKey, key) = NewKeyPair();
        var signedNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var signature = Sign(key, signedNonce);

        var differentNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Assert.False(BiometricSignature.Verify(publicKey, differentNonce, signature));
    }

    [Theory]
    [InlineData("khong-phai-base64!!!", "AAAA", "AAAA")]
    [InlineData(null, "AAAA", "AAAA")]
    public void Input_hong_thi_tra_false_khong_nem_loi(string? publicKey, string nonce, string signature)
    {
        Assert.False(BiometricSignature.Verify(publicKey ?? "", nonce, signature));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~BiometricSignatureTests`
Expected: FAIL — `BiometricSignature` does not exist yet.

- [ ] **Step 3: Write `BiometricSignature.cs`**

```csharp
using System.Security.Cryptography;

namespace AioKin.Services.Auth.Biometric;

/// <summary>
/// Verify chu ky ECDSA P-256 cho dang nhap sinh trac. Dinh dang DER
/// (Rfc3279DerSequence) — Android SHA256withECDSA va iOS SecKeyCreateSignature deu xuat
/// dinh dang nay mac dinh, khac IEEE P1363 la mac dinh cua .NET.
/// </summary>
public static class BiometricSignature
{
    public static bool Verify(string publicKeyBase64, string nonceBase64, string signatureBase64)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);

            var nonceBytes = Convert.FromBase64String(nonceBase64);
            var signatureBytes = Convert.FromBase64String(signatureBase64);

            return ecdsa.VerifyData(nonceBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            // Du lieu tu client khong dang tin: base64 hong, key sai dinh dang/duong cong,
            // chu ky sai do dai — tat ca deu la "khong hop le", khong phai loi he thong.
            return false;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~BiometricSignatureTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(auth): add pure ECDSA signature verification for biometric login"
```

---

### Task 3: `IBiometricAuthService` / `BiometricAuthService`

**Files:**
- Create: `AioKin/Services/Auth/Biometric/BiometricChallenge.cs`
- Create: `AioKin/Services/Auth/Biometric/IBiometricAuthService.cs`
- Create: `AioKin/Services/Auth/Biometric/BiometricAuthService.cs`
- Modify: `AioKin/Common/RedisKeys.cs`
- Modify: `AioKin/Program.cs` (DI registration)

**Interfaces:**
- Consumes: `AioKinDbContext.DeviceCredentials`/`Users` (Task 1), `BiometricSignature.Verify` (Task 2), `IAccessTokenService.CreateForCustomerAsync(UserDb, DeviceInfo)`, `IRefreshTokenService.GenerateAsync(string, string, string?)`, `DeviceInfo` (from `docs/superpowers/plans/2026-09-25-token-session-management.md`), `IUserService.GetByUserCodeAsync`.
- Produces: `IBiometricAuthService` with `RegisterAsync`, `ChallengeAsync`, `VerifyAsync`.

- [ ] **Step 1: `BiometricChallenge.cs`**

```csharp
namespace AioKin.Services.Auth.Biometric;

/// <summary>Du lieu tam luu trong Redis giua buoc challenge va verify.</summary>
public sealed record BiometricChallenge(string UserCode, string DeviceId, string Nonce);
```

- [ ] **Step 2: Add the Redis key/TTL to `RedisKeys.cs`**

```csharp
    /// <summary>Challenge dang cho verify, dung 1 lan.</summary>
    public static string BiometricChallenge(string challengeId) => $"auth:biometric_challenge:{challengeId}";
```

and in `RedisTtl`:

```csharp
    public static readonly TimeSpan BiometricChallenge = TimeSpan.FromMinutes(2);
```

- [ ] **Step 3: `IBiometricAuthService.cs`**

```csharp
using AioKin.Models.InputModel.Auth.Biometric;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Auth.Biometric;
using AioKin.Models.ViewModel.Auth.User;

namespace AioKin.Services.Auth.Biometric;

public interface IBiometricAuthService
{
    /// <summary>Dang ky/cap nhat public key cua mot thiet bi cho user dang dang nhap.</summary>
    Task<OperationResult> RegisterAsync(Guid userId, RegisterBiometricRequest request);

    /// <summary>Luon tra ve mot challenge moi, bat ke (userCode, deviceId) co dang ky hay chua.</summary>
    Task<BiometricChallengeResponse> ChallengeAsync(BiometricChallengeRequest request);

    /// <summary>Xac thuc chu ky va phat token neu hop le. Data la TokenResponse khi Success.</summary>
    Task<OperationResult> VerifyAsync(BiometricVerifyRequest request);

    /// <summary>Thu hoi (revoked_at) credential cua chinh user cho 1 deviceId.</summary>
    Task<OperationResult> RevokeAsync(Guid userId, string deviceId);
}
```

- [ ] **Step 4: `BiometricAuthService.cs`**

```csharp
using System.Security.Cryptography;
using AioKin.Common;
using AioKin.Data;
using AioKin.Data.Entities.Security;
using AioKin.Models.InputModel.Auth.Biometric;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Auth.Biometric;
using AioKin.Models.ViewModel.Auth.User;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.Token;
using AioKin.Services.Auth.User;
using AioKin.Services.Common.Cache;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Auth.Biometric;

public class BiometricAuthService : IBiometricAuthService
{
    private readonly AioKinDbContext _db;
    private readonly IUserService _userService;
    private readonly IRedisService _redis;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ILogger<BiometricAuthService> _logger;

    public BiometricAuthService(
        AioKinDbContext db,
        IUserService userService,
        IRedisService redis,
        IAccessTokenService accessTokenService,
        IRefreshTokenService refreshTokenService,
        ILogger<BiometricAuthService> logger)
    {
        _db = db;
        _userService = userService;
        _redis = redis;
        _accessTokenService = accessTokenService;
        _refreshTokenService = refreshTokenService;
        _logger = logger;
    }

    public async Task<OperationResult> RegisterAsync(Guid userId, RegisterBiometricRequest request)
    {
        var existing = await _db.DeviceCredentials
            .FirstOrDefaultAsync(c => c.UserID == userId && c.DeviceId == request.DeviceId);

        if (existing is not null)
        {
            // Doi key (cai lai app, xoay key) — ghi de, khong tao dong moi, va mo lai neu
            // truoc do da bi revoke.
            existing.PublicKey = request.PublicKey;
            existing.DeviceName = request.DeviceName;
            existing.Platform = request.Platform;
            existing.RevokedAt = null;
        }
        else
        {
            _db.DeviceCredentials.Add(new DeviceCredential
            {
                UserID = userId,
                DeviceId = request.DeviceId,
                DeviceName = request.DeviceName,
                Platform = request.Platform,
                PublicKey = request.PublicKey
            });
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Biometric credential registered for userId={UserId}, deviceId={DeviceId}", userId, request.DeviceId);
        return OperationResult.Ok("Da dang ky dang nhap sinh trac cho thiet bi nay.");
    }

    public async Task<BiometricChallengeResponse> ChallengeAsync(BiometricChallengeRequest request)
    {
        var challengeId = Guid.NewGuid().ToString("N");
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        // Luon luu va tra ve, KHONG kiem tra credential co ton tai o day: kiem tra som se
        // lo (userCode, deviceId) nao dang co dang ky sinh trac qua response 200/khac.
        await _redis.SetAsync(
            RedisKeys.BiometricChallenge(challengeId),
            new BiometricChallenge(request.UserCode, request.DeviceId, nonce),
            RedisTtl.BiometricChallenge);

        return new BiometricChallengeResponse { ChallengeId = challengeId, Nonce = nonce };
    }

    public async Task<OperationResult> VerifyAsync(BiometricVerifyRequest request)
    {
        var key = RedisKeys.BiometricChallenge(request.ChallengeId);
        var challenge = await _redis.GetAsync<BiometricChallenge>(key);

        // Xoa NGAY sau khi doc, truoc khi verify: dung 1 lan, chan replay va chan hai
        // request verify chay song song tren cung mot challenge.
        await _redis.DeleteAsync(key);

        if (challenge is null)
            return Fail();

        var user = await _userService.GetByUserCodeAsync(challenge.UserCode);
        if (user is null || !user.IsActive || user.IsLocked)
            return Fail();

        var credential = await _db.DeviceCredentials
            .FirstOrDefaultAsync(c => c.UserID == user.UserID && c.DeviceId == challenge.DeviceId && c.RevokedAt == null);
        if (credential is null)
            return Fail();

        if (!BiometricSignature.Verify(credential.PublicKey, challenge.Nonce, request.Signature))
            return Fail();

        credential.LastUsedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var device = new DeviceInfo(challenge.DeviceId, credential.DeviceName, credential.Platform);

        _logger.LogInformation("Biometric login succeeded for userCode={UserCode}, deviceId={DeviceId}", challenge.UserCode, challenge.DeviceId);

        return OperationResult.Ok("Dang nhap thanh cong.", new TokenResponse
        {
            AccessToken = await _accessTokenService.CreateForCustomerAsync(user, device),
            RefreshToken = await _refreshTokenService.GenerateAsync(user.UserCode, Roles.CUSTOMER, challenge.DeviceId),
            ExpiresIn = _accessTokenService.AccessTokenLifetimeSeconds,
            TokenType = "Bearer",
            Scope = Roles.CUSTOMER
        });

        // Mot diem loi duy nhat cho moi truong hop that bai — khong lo buoc nao trong so
        // "challenge het han" / "khong co credential" / "chu ky sai" la nguyen nhan that.
        static OperationResult Fail() => OperationResult.Fail("InvalidCredentials", "Khong dang nhap duoc bang sinh trac hoc.");
    }

    public async Task<OperationResult> RevokeAsync(Guid userId, string deviceId)
    {
        var credential = await _db.DeviceCredentials
            .FirstOrDefaultAsync(c => c.UserID == userId && c.DeviceId == deviceId && c.RevokedAt == null);

        if (credential is null)
            return OperationResult.Fail("NotFound", "Khong tim thay dang ky sinh trac cho thiet bi nay.");

        credential.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Biometric credential revoked for userId={UserId}, deviceId={DeviceId}", userId, deviceId);
        return OperationResult.Ok("Da tat dang nhap sinh trac cho thiet bi nay.");
    }
}
```

- [ ] **Step 5: Register in `Program.cs`**

Add next to the other `─── Service tang Auth ───` registrations:

```csharp
builder.Services.AddScoped<IBiometricAuthService, BiometricAuthService>();
```

Add `using AioKin.Services.Auth.Biometric;` to the top of `Program.cs`.

- [ ] **Step 6: Build**

Run: `dotnet build AioKin/AioKin.csproj`
Expected: fails only on the missing `AioKin.Models.InputModel.Auth.Biometric`/`AioKin.Models.ViewModel.Auth.Biometric` namespaces — Task 4 adds those DTOs. This is expected at this point; do not skip ahead, Task 4 is next.

- [ ] **Step 7: Commit** (after Task 4's DTOs exist and this builds — see Task 4 Step 4)

---

### Task 4: DTOs + `BiometricController` + integration tests

**Files:**
- Create: `AioKin/Models/InputModel/Auth/Biometric/BiometricRequests.cs`
- Create: `AioKin/Models/ViewModel/Auth/Biometric/BiometricChallengeResponse.cs`
- Create: `AioKin/Controllers/Auth/BiometricController.cs`
- Create: `AioKin.Tests/Auth/BiometricAuthTests.cs`

**Interfaces:**
- Consumes: `IBiometricAuthService` (Task 3).

- [ ] **Step 1: `BiometricRequests.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Auth.Biometric;

public class RegisterBiometricRequest
{
    [Required(ErrorMessage = "DeviceId la bat buoc.")]
    [MaxLength(100)]
    public string DeviceId { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? DeviceName { get; set; }

    [MaxLength(20)]
    public string? Platform { get; set; }

    [Required(ErrorMessage = "PublicKey la bat buoc.")]
    public string PublicKey { get; set; } = string.Empty;
}

public class BiometricChallengeRequest
{
    [Required(ErrorMessage = "UserCode la bat buoc.")]
    public string UserCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "DeviceId la bat buoc.")]
    public string DeviceId { get; set; } = string.Empty;
}

public class BiometricVerifyRequest
{
    [Required(ErrorMessage = "ChallengeId la bat buoc.")]
    public string ChallengeId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Signature la bat buoc.")]
    public string Signature { get; set; } = string.Empty;
}
```

- [ ] **Step 2: `BiometricChallengeResponse.cs`**

```csharp
namespace AioKin.Models.ViewModel.Auth.Biometric;

public class BiometricChallengeResponse
{
    public string ChallengeId { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
}
```

- [ ] **Step 3: `BiometricController.cs`**

```csharp
using AioKin.Common;
using AioKin.Models.InputModel.Auth.Biometric;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Services.Auth.Biometric;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AioKin.Controllers.Auth;

/// <summary>
/// Dang nhap bang sinh trac hoc (Face ID/van tay). Server khong bao gio nhan du lieu sinh
/// trac — chi verify chu ky ECDSA cua mot challenge dung mot lan. Xem
/// docs/auth-opaque-tokens-biometric.md muc 5 cho toan bo chuoi buoc.
/// </summary>
[ApiController]
[Route("auth/biometric")]
[Produces("application/json")]
public class BiometricController : ControllerBase
{
    private readonly IBiometricAuthService _biometricAuthService;

    public BiometricController(IBiometricAuthService biometricAuthService)
    {
        _biometricAuthService = biometricAuthService;
    }

    /// <summary>Dang ky public key cua thiet bi hien tai. Can da dang nhap thuong truoc do.</summary>
    [HttpPost("register")]
    [Authorize(Roles = Roles.CUSTOMER)]
    public async Task<IActionResult> Register([FromBody] RegisterBiometricRequest request)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return Unauthorized(OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        // RegisterAsync can UserID noi bo, khong phai UUID cong khai — nhung DeviceCredential
        // luu UserID, nen tra cuu qua UserService truoc.
        return this.ToActionResult(await _biometricAuthService.RegisterAsync(userUuid.Value, request));
    }

    /// <summary>Xin mot challenge de dang nhap bang sinh trac. Luon tra 200, bat ke thiet bi co dang ky hay chua.</summary>
    [HttpPost("challenge")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Challenge([FromBody] BiometricChallengeRequest request)
        => Ok(await _biometricAuthService.ChallengeAsync(request));

    /// <summary>Xac thuc chu ky va phat token neu hop le.</summary>
    [HttpPost("verify")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Verify([FromBody] BiometricVerifyRequest request)
        => this.ToActionResult(await _biometricAuthService.VerifyAsync(request));

    /// <summary>Tat dang nhap sinh trac cho 1 thiet bi (mat may, doi thiet bi).</summary>
    [HttpDelete("{deviceId}")]
    [Authorize(Roles = Roles.CUSTOMER)]
    public async Task<IActionResult> Revoke(string deviceId)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return Unauthorized(OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        return this.ToActionResult(await _biometricAuthService.RevokeAsync(userUuid.Value, deviceId));
    }
}
```

`RegisterAsync`/`RevokeAsync` on `IBiometricAuthService` take a `Guid userId` — this is the *internal* `UserID`, but the controller only has `UserUuid` from the token (Customer tokens never carry the internal `UserID`, only `UserUuid`/`UserCode`, per `AccessTokenSession`). Fix this mismatch now, before it compiles wrong: change `IBiometricAuthService.RegisterAsync`/`RevokeAsync` to take `Guid userUuid` instead of `Guid userId`, and resolve to the internal `UserID` inside `BiometricAuthService` via `_userService.GetByUuidAsync(userUuid)` at the top of each method (returning `OperationResult.Fail("NotFound", ...)` if it comes back null). Apply this correction to Task 3's `BiometricAuthService.cs` before running this task's tests.

- [ ] **Step 4: Build**

Run: `dotnet build AioKin/AioKin.csproj`
Expected: succeeds now that Task 3's `BiometricAuthService` and this task's DTOs both exist. Commit Task 3 and Task 4 together if you split the work this way — they only compile as a unit.

- [ ] **Step 5: Write the integration tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using AioKin.Models.ViewModel.Auth.Biometric;
using AioKin.Models.ViewModel.Auth.User;
using AioKin.Tests.Infrastructure;
using Xunit;

namespace AioKin.Tests.Auth;

[Collection(ApiCollection.Name)]
public class BiometricAuthTests
{
    private readonly ApiFixture _fixture;

    public BiometricAuthTests(ApiFixture fixture) => _fixture = fixture;

    private static (string PublicKeyBase64, ECDsa Key) NewKeyPair()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), key);
    }

    private static string Sign(ECDsa key, string nonceBase64)
        => Convert.ToBase64String(key.SignData(
            Convert.FromBase64String(nonceBase64), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    private async Task<(TestUser User, string UserCode, string DeviceId)> RegisterDeviceAsync(string publicKey)
    {
        var testUser = await TestUser.CreateAsync(_fixture);
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = await db.Users.FindAsync(testUser.UserId);
        var deviceId = $"device-{Guid.NewGuid():N}";

        var registerResponse = await testUser.Client.PostAsJsonAsync("/auth/biometric/register", new
        {
            deviceId,
            deviceName = "Test Phone",
            platform = "android",
            publicKey
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        return (testUser, user!.UserCode, deviceId);
    }

    [Fact]
    public async Task Dang_ky_roi_dang_nhap_sinh_trac_thanh_cong_phat_ra_token_dung()
    {
        var (publicKey, key) = NewKeyPair();
        var (_, userCode, deviceId) = await RegisterDeviceAsync(publicKey);
        var anonymousClient = _fixture.CreateClient();

        var challengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<BiometricChallengeResponse>();

        var signature = Sign(key, challenge!.Nonce);
        var verifyResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = challenge.ChallengeId, signature });

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var body = await verifyResponse.Content.ReadFromJsonAsync<OperationResultOf<TokenResponse>>();
        Assert.True(body!.Success);
        Assert.False(string.IsNullOrEmpty(body.Data!.AccessToken));
    }

    [Fact]
    public async Task Chu_ky_sai_key_thi_verify_that_bai()
    {
        var (publicKey, _) = NewKeyPair();
        var (_, wrongKey) = NewKeyPair();
        var (_, userCode, deviceId) = await RegisterDeviceAsync(publicKey);
        var anonymousClient = _fixture.CreateClient();

        var challengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<BiometricChallengeResponse>();

        var signature = Sign(wrongKey, challenge!.Nonce);
        var verifyResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = challenge.ChallengeId, signature });

        Assert.Equal(HttpStatusCode.Unauthorized, verifyResponse.StatusCode);
    }

    [Fact]
    public async Task Dung_lai_cung_challengeId_lan_thu_hai_thi_that_bai()
    {
        var (publicKey, key) = NewKeyPair();
        var (_, userCode, deviceId) = await RegisterDeviceAsync(publicKey);
        var anonymousClient = _fixture.CreateClient();

        var challengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<BiometricChallengeResponse>();
        var signature = Sign(key, challenge!.Nonce);

        var first = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify", new { challengeId = challenge.ChallengeId, signature });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify", new { challengeId = challenge.ChallengeId, signature });
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact]
    public async Task Challenge_cho_thiet_bi_chua_dang_ky_van_tra_200()
    {
        var anonymousClient = _fixture.CreateClient();

        var response = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge",
            new { userCode = "UC-KHONG-TON-TAI", deviceId = "device-la" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Thu_hoi_credential_roi_verify_thi_that_bai()
    {
        var (publicKey, key) = NewKeyPair();
        var (testUser, userCode, deviceId) = await RegisterDeviceAsync(publicKey);
        var anonymousClient = _fixture.CreateClient();

        var revokeResponse = await testUser.Client.DeleteAsync($"/auth/biometric/{deviceId}");
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        var challengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<BiometricChallengeResponse>();
        var signature = Sign(key, challenge!.Nonce);

        var verifyResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = challenge.ChallengeId, signature });

        Assert.Equal(HttpStatusCode.Unauthorized, verifyResponse.StatusCode);
    }
}
```

`OperationResultOf<T>` is the small helper type already added in `docs/superpowers/plans/2026-09-25-token-session-management.md` Task 3 — reuse it, do not redefine it in this file (it would collide).

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~BiometricAuthTests`
Expected: PASS (5 tests).

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 8: Run `detect_changes()` per `CLAUDE.md`**

`detect_changes({scope: "compare", base_ref: "feat/token-session-management"})` — confirm only this plan's File Structure files changed.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(auth): add biometric device login (register/challenge/verify/revoke)"
```

---

## After this plan

All three plans together give: opaque, instantly-revocable access tokens; refresh tokens and access sessions both tagged by device with self-service listing/revoke; and a challenge/response biometric login that never touches raw biometric data and reuses the same token-issuing path as every other login method. The Android/iOS side (repos `NativeKotlin`, `IOSAPP`) is a separate, client-only follow-up: generate a stable per-install `deviceId`, create the ECDSA P-256 keypair in Keystore/Secure Enclave with `setUserAuthenticationRequired`, and call these four endpoints — none of that touches this repo.
