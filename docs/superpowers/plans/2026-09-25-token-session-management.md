# Token Session Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tag every access/refresh token with the device that requested it, and let a customer see which devices are logged in and log one of them out remotely — without touching the others.

**Architecture:** `LoginRequest`/`RefreshTokenRequest`/`VerifyOtpRequest` gain an optional `DeviceId`/`DeviceName`/`Platform`. `AccessTokenSession` and the refresh token's Redis payload both carry the resolved device info (a client that omits `deviceId` gets a server-generated one, so a session always has *some* identity to show). `GET /account/sessions` lists the caller's live access sessions; `DELETE /account/sessions/{id}` revokes one access session *and* every refresh token tagged with the same device — otherwise the refresh token would silently mint a fresh access token minutes later and the "logout" would not have logged anything out. Refresh token storage is migrated from a raw-token Redis key to a `sha256(token)` key at the same time, for the same reason `AccessTokenService` already hashes (a Redis dump must not hand out live, directly-usable session tokens).

**Tech Stack:** .NET 9, `IRedisService` (existing 3-tier abstraction), xUnit + `MemoryCacheRedisService` for unit tests, xUnit + `Microsoft.AspNetCore.Mvc.Testing`/`ApiFixture` for integration tests.

**Spec:** `docs/auth-opaque-tokens-biometric.md` — section 4 (Phase B target), section 6 (API table rows for `/account/sessions`), section 7 (locked decisions, rows "Định danh session cong khai" and "Revoke 1 thiet bi").

## Global Constraints

- **Comments and docs are Vietnamese without diacritics** ("khong dau").
- **This plan starts only after `docs/superpowers/plans/2026-09-25-opaque-access-tokens.md` is merged** — it depends on `IAccessTokenService`/`AccessTokenService` existing exactly as that plan leaves them.
- **`deviceId` is always optional from the client.** Every DTO change in this plan must keep existing callers (the Android app, which does not send it yet) working unchanged — omission must not become a validation error.
- **The public session id must never let anyone recover the raw token or another user's session.** It is `sha256(token)` truncated to 12 hex chars, and `DELETE /account/sessions/{id}` must check the id belongs to the caller's own tracked session set before deleting anything.
- **Session endpoints in this plan are Customer-only** (`AccountController`, already `[Authorize(Roles = Roles.CUSTOMER)]`). Staff-side session management is not in scope here.
- **Run `impact({target: "RefreshTokenService", direction: "upstream", repo: "AioKin"})` before Task 2** — 7 call sites depend on `IRefreshTokenService`'s exact method signatures; this plan changes them.
- **Run `detect_changes()` before the final commit.**

## Review Focus

- **A client that never sends `deviceId`** must still get a working login/refresh/session — the session simply shows as "Unknown device", never a 400/500.
- **`DELETE /account/sessions/{id}` with an id belonging to a different user** must return 404 (indistinguishable from "id doesn't exist"), never leak whether that id exists for someone else.
- **Revoking a device must kill its refresh token too**, not just the access session — otherwise the device keeps working for up to `Jwt:RefreshTokenExpiryDays` after the user thought they logged it out. This is the entire point of the plan; a test must prove it (refresh with the revoked device's old refresh token fails after the delete).
- **The "current" session** (the one the caller is using right now) must be identifiable in the `GET /account/sessions` list (`isCurrent: true`) so the UI can warn before letting someone revoke their own active session.
- **Migrating `RefreshTokenService`'s storage key from raw-token to hashed** must not orphan tokens issued before the migration — since Redis TTL is at most 7 days and this is a fresh feature branch, no data-migration step is needed, but the plan's tests must exercise the new format end-to-end, not assume the old format still decodes.

---

## Prerequisites

Branch from the finished opaque-access-tokens branch (per `docs/git-flow.md`):

```bash
git checkout feat/opaque-access-tokens
git pull
git checkout -b feat/token-session-management
```

---

## File Structure

**New:**

| File | Responsibility |
|---|---|
| `AioKin/Common/TokenHash.cs` | Shared `sha256(token)` hex helper — extracted from `AccessTokenService`, reused by `RefreshTokenService` |
| `AioKin/Services/Auth/RefreshToken/RefreshTokenPayload.cs` | JSON payload stored per refresh token (subject, role, device id) |
| `AioKin/Services/Auth/Token/DeviceInfo.cs` | `record DeviceInfo(string? DeviceId, string? DeviceName, string? Platform)` + `.Resolve()` fallback |
| `AioKin/Models/ViewModel/Auth/User/SessionResponse.cs` | `GET /account/sessions` item shape |
| `AioKin.Tests/Auth/RefreshTokenServiceTests.cs` | Unit tests, `MemoryCacheRedisService` |
| `AioKin.Tests/Auth/SessionManagementTests.cs` | Integration tests through `ApiFixture` |

**Modified:**

| File | Change |
|---|---|
| `AioKin/Services/Auth/Token/AccessTokenService.cs` | Use `TokenHash` instead of its private copy; accept `DeviceInfo`; add `ListSessionsAsync` |
| `AioKin/Services/Auth/Token/IAccessTokenService.cs` | `CreateForCustomerAsync`/`CreateForStaffAsync` take a `DeviceInfo`; add `ListSessionsAsync`, `RevokeByHashPrefixAsync` |
| `AioKin/Services/Auth/Token/AccessTokenSession.cs` | Add `DeviceId`, `DeviceName`, `Platform` |
| `AioKin/Services/Auth/RefreshToken/IRefreshTokenService.cs` | `GenerateAsync` takes `deviceId`; `ValidateAsync` returns `RefreshTokenPayload?`; add `RevokeAllForDeviceAsync` |
| `AioKin/Services/Auth/RefreshToken/RefreshTokenService.cs` | Hashed key, JSON payload, device-scoped revoke |
| `AioKin/Models/InputModel/Auth/User/LoginRequest.cs` | `LoginRequest`, `RefreshTokenRequest` gain `DeviceId?`, `DeviceName?`, `Platform?` |
| `AioKin/Models/InputModel/Auth/User/OtpRequests.cs` | `VerifyOtpRequest` gains the same three fields |
| `AioKin/Controllers/Auth/AuthController.cs` | Build `DeviceInfo` from the request in `Login`/`VerifyOtp`/`RefreshToken`, pass to both token services |
| `AioKin/Services/Auth/Admin/AdminAuthService.cs` | `LoginAdminRequest` path — pass `DeviceInfo.Resolve(null, null, null)` (admin login has no device fields yet; out of scope to add them to the admin DTO in this plan) |
| `AioKin/Services/Auth/OAuth/OAuthService.cs` | Pass `DeviceInfo.Resolve(null, null, null)` — SSO popup has no device fields to read from |
| `AioKin/Controllers/Account/AccountController.cs` | Add `IAccessTokenService`, `IRefreshTokenService`-based `GET /account/sessions`, `DELETE /account/sessions/{id}` |
| `AioKin.Tests/Infrastructure/TestUser.cs` | Pass `DeviceInfo.Unknown` to `CreateForCustomerAsync` |

---

### Task 1: Extract `TokenHash`, migrate `RefreshTokenService` to hashed keys + JSON payload + device tag

**Files:**
- Create: `AioKin/Common/TokenHash.cs`
- Create: `AioKin/Services/Auth/RefreshToken/RefreshTokenPayload.cs`
- Modify: `AioKin/Services/Auth/RefreshToken/IRefreshTokenService.cs`
- Modify: `AioKin/Services/Auth/RefreshToken/RefreshTokenService.cs`
- Modify: `AioKin/Services/Auth/Token/AccessTokenService.cs` (use the shared helper instead of its own copy)
- Create: `AioKin.Tests/Auth/RefreshTokenServiceTests.cs`

**Interfaces:**
- Produces: `TokenHash.Sha256Hex(string token)`, `RefreshTokenPayload(string Subject, string Role, string? DeviceId)`, `IRefreshTokenService.GenerateAsync(string subject, string role, string? deviceId)`, `IRefreshTokenService.ValidateAsync(string token) : Task<RefreshTokenPayload?>`, `IRefreshTokenService.RevokeAllForDeviceAsync(string subject, string? deviceId)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Common.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AioKin.Tests.Auth;

public class RefreshTokenServiceTests
{
    private static RefreshTokenService CreateSut()
    {
        var redis = new MemoryCacheRedisService(new MemoryCache(new MemoryCacheOptions()), NullLogger<MemoryCacheRedisService>.Instance);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        return new RefreshTokenService(redis, config, NullLogger<RefreshTokenService>.Instance);
    }

    [Fact]
    public async Task GenerateAsync_roi_ValidateAsync_tra_ve_dung_subject_role_deviceId()
    {
        var sut = CreateSut();

        var token = await sut.GenerateAsync("UC123", "Customer", "device-abc");
        var payload = await sut.ValidateAsync(token);

        Assert.NotNull(payload);
        Assert.Equal("UC123", payload!.Subject);
        Assert.Equal("Customer", payload.Role);
        Assert.Equal("device-abc", payload.DeviceId);
    }

    [Fact]
    public async Task GenerateAsync_khong_can_deviceId()
    {
        var sut = CreateSut();

        var token = await sut.GenerateAsync("UC123", "Customer", deviceId: null);
        var payload = await sut.ValidateAsync(token);

        Assert.NotNull(payload);
        Assert.Null(payload!.DeviceId);
    }

    [Fact]
    public async Task RevokeAllForDeviceAsync_chi_thu_hoi_token_cua_dung_thiet_bi()
    {
        var sut = CreateSut();
        var tokenDeviceA = await sut.GenerateAsync("UC1", "Customer", "device-a");
        var tokenDeviceB = await sut.GenerateAsync("UC1", "Customer", "device-b");

        await sut.RevokeAllForDeviceAsync("UC1", "device-a");

        Assert.Null(await sut.ValidateAsync(tokenDeviceA));
        Assert.NotNull(await sut.ValidateAsync(tokenDeviceB));
    }

    [Fact]
    public async Task RevokeAllAsync_thu_hoi_moi_thiet_bi()
    {
        var sut = CreateSut();
        var tokenA = await sut.GenerateAsync("UC1", "Customer", "device-a");
        var tokenB = await sut.GenerateAsync("UC1", "Customer", "device-b");

        await sut.RevokeAllAsync("UC1");

        Assert.Null(await sut.ValidateAsync(tokenA));
        Assert.Null(await sut.ValidateAsync(tokenB));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~RefreshTokenServiceTests`
Expected: FAIL — compile error, `GenerateAsync` does not yet take a `deviceId`, `ValidateAsync` does not yet return `RefreshTokenPayload`.

- [ ] **Step 3: `TokenHash.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace AioKin.Common;

/// <summary>
/// Bam token truoc khi dung lam Redis key, cho ca access va refresh token: mot ban
/// dump/backup Redis khong duoc de lo session dang song duoi dang dung duoc luon.
/// </summary>
public static class TokenHash
{
    public static string Sha256Hex(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
```

- [ ] **Step 4: `RefreshTokenPayload.cs`**

```csharp
namespace AioKin.Services.Auth.RefreshToken;

/// <summary>Du lieu luu kem refresh token trong Redis, duoi khoa sha256(token).</summary>
public sealed record RefreshTokenPayload(string Subject, string Role, string? DeviceId);
```

- [ ] **Step 5: Update `IRefreshTokenService.cs`**

```csharp
namespace AioKin.Services.Auth.RefreshToken;

/// <summary>
/// Refresh token opaque, luu trong Redis duoi khoa sha256(token). Trang thai nam o server
/// nen thu hoi co hieu luc ngay lap tuc.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Tao token moi va luu vao Redis. <paramref name="deviceId"/> co the null (client cu chua gui).</summary>
    Task<string> GenerateAsync(string subject, string role, string? deviceId);

    /// <summary>Xac thuc. Null neu het han hoac khong ton tai.</summary>
    Task<RefreshTokenPayload?> ValidateAsync(string token);

    /// <summary>Thu hoi mot token cu the — dung khi logout hoac khi xoay vong token.</summary>
    Task RevokeAsync(string token);

    /// <summary>Thu hoi tat ca token cua mot subject — dung khi doi mat khau hoac nghi ngo lo tai khoan.</summary>
    Task RevokeAllAsync(string subject);

    /// <summary>Thu hoi tat ca token cua mot subject PHAT TU mot thiet bi cu the — dung khi "dang xuat thiet bi nay".</summary>
    Task RevokeAllForDeviceAsync(string subject, string? deviceId);
}
```

- [ ] **Step 6: Rewrite `RefreshTokenService.cs`**

```csharp
using AioKin.Common;
using AioKin.Services.Common.Cache;

namespace AioKin.Services.Auth.RefreshToken;

public class RefreshTokenService : IRefreshTokenService
{
    private readonly IRedisService _redis;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RefreshTokenService> _logger;

    private const int MaxTokensPerUser = 10;
    private const char TokenSeparator = '\n';

    public RefreshTokenService(IRedisService redis, IConfiguration configuration, ILogger<RefreshTokenService> logger)
    {
        _redis = redis;
        _configuration = configuration;
        _logger = logger;
    }

    private TimeSpan ResolveTtl()
    {
        var days = int.TryParse(_configuration["Jwt:RefreshTokenExpiryDays"], out var d) && d > 0
            ? d
            : (int)RedisTtl.RefreshToken.TotalDays;
        return TimeSpan.FromDays(days);
    }

    public async Task<string> GenerateAsync(string subject, string role, string? deviceId)
    {
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var hash = TokenHash.Sha256Hex(token);
        var ttl = ResolveTtl();

        await _redis.SetAsync(RedisKeys.RefreshToken(hash), new RefreshTokenPayload(subject, role, deviceId), ttl);
        await TrackTokenAsync(subject, hash, ttl);

        _logger.LogInformation("Refresh token generated for subject={Subject}, device={DeviceId}", subject, deviceId ?? "unknown");
        return token;
    }

    private async Task TrackTokenAsync(string subject, string hash, TimeSpan ttl)
    {
        var userTokensKey = RedisKeys.UserRefreshTokens(subject);
        var existing = await _redis.GetStringAsync(userTokensKey) ?? string.Empty;
        var hashes = existing.Split(TokenSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        hashes.Add(hash);

        if (hashes.Count > MaxTokensPerUser)
        {
            foreach (var evicted in hashes[..^MaxTokensPerUser])
                await _redis.DeleteAsync(RedisKeys.RefreshToken(evicted));

            hashes = hashes[^MaxTokensPerUser..];
        }

        await _redis.SetStringAsync(userTokensKey, string.Join(TokenSeparator, hashes), ttl);
    }

    public Task<RefreshTokenPayload?> ValidateAsync(string token)
        => _redis.GetAsync<RefreshTokenPayload>(RedisKeys.RefreshToken(TokenHash.Sha256Hex(token)));

    public async Task RevokeAsync(string token)
    {
        await _redis.DeleteAsync(RedisKeys.RefreshToken(TokenHash.Sha256Hex(token)));
        _logger.LogInformation("Refresh token revoked");
    }

    public async Task RevokeAllAsync(string subject)
    {
        var userTokensKey = RedisKeys.UserRefreshTokens(subject);
        var existing = await _redis.GetStringAsync(userTokensKey) ?? string.Empty;

        foreach (var hash in existing.Split(TokenSeparator, StringSplitOptions.RemoveEmptyEntries))
            await _redis.DeleteAsync(RedisKeys.RefreshToken(hash));

        await _redis.DeleteAsync(userTokensKey);
        _logger.LogInformation("All refresh tokens revoked for subject={Subject}", subject);
    }

    public async Task RevokeAllForDeviceAsync(string subject, string? deviceId)
    {
        var userTokensKey = RedisKeys.UserRefreshTokens(subject);
        var existing = await _redis.GetStringAsync(userTokensKey) ?? string.Empty;

        // Cac hash cua thiet bi bi thu hoi duoc de lai trong danh sach — chung se tu that
        // bai o ValidateAsync (key AccessSession/RefreshToken da bi xoa) va bi day ra dan
        // qua MaxTokensPerUser hoac qua lan RevokeAllAsync ke tiep. Khong dang lam sach ngay
        // vi phai doc lai tung payload chi de biet hash nao thuoc thiet bi nao.
        foreach (var hash in existing.Split(TokenSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var payload = await _redis.GetAsync<RefreshTokenPayload>(RedisKeys.RefreshToken(hash));
            if (payload is not null && string.Equals(payload.DeviceId, deviceId, StringComparison.Ordinal))
                await _redis.DeleteAsync(RedisKeys.RefreshToken(hash));
        }

        _logger.LogInformation("Refresh tokens revoked for subject={Subject}, device={DeviceId}", subject, deviceId ?? "unknown");
    }
}
```

- [ ] **Step 7: Update `AccessTokenService.cs` to use the shared helper**

Delete its private `HashToken` method and replace both call sites (`IssueAsync`, `ValidateAsync`, `RevokeAsync`) with `TokenHash.Sha256Hex(...)`. Add `using AioKin.Common;` if not already present (it already is).

- [ ] **Step 8: Fix the caller in `AuthController.RefreshToken`**

The existing destructuring `var (subject, role) = payload.Value;` no longer compiles (`RefreshTokenPayload` is a reference type, not a nullable value tuple). Change to:

```csharp
        var payload = await _refreshTokenService.ValidateAsync(model.RefreshToken);
        if (payload is null)
            return this.ToActionResult(OperationResult.Fail("InvalidRefreshToken",
                "Refresh token khong hop le hoac da het han. Vui long dang nhap lai."));

        var (subject, role, deviceId) = payload;
```

(the rest of the method is unchanged for now — Task 3 threads `deviceId` through to the new `GenerateAsync`/`CreateForCustomerAsync`/`CreateForStaffAsync` calls in this same method).

- [ ] **Step 9: Fix every other `GenerateAsync` call site to compile**

Each of `AuthController.Login`, `AuthController.VerifyOtp`, `AuthController.RefreshToken`, `AdminAuthService.LoginAsync`, `OAuthService`'s token block currently calls `_refreshTokenService.GenerateAsync(subject, role)` with two arguments. Add a third argument `deviceId: null` at each of these five call sites for now, so the build is green — Task 3 replaces `null` with a real resolved `DeviceInfo.DeviceId` at each site.

- [ ] **Step 10: Run tests**

Run: `dotnet test`
Expected: PASS — new `RefreshTokenServiceTests` plus every existing test (they all still log in with `deviceId: null`, which is a supported value).

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "refactor(auth): hash refresh token keys, carry device id in the payload"
```

---

### Task 2: `DeviceInfo`, device fields on `AccessTokenSession`, request DTOs

**Files:**
- Create: `AioKin/Services/Auth/Token/DeviceInfo.cs`
- Modify: `AioKin/Services/Auth/Token/AccessTokenSession.cs`
- Modify: `AioKin/Services/Auth/Token/IAccessTokenService.cs`
- Modify: `AioKin/Services/Auth/Token/AccessTokenService.cs`
- Modify: `AioKin/Models/InputModel/Auth/User/LoginRequest.cs`
- Modify: `AioKin/Models/InputModel/Auth/User/OtpRequests.cs`

**Interfaces:**
- Produces: `DeviceInfo(string? DeviceId, string? DeviceName, string? Platform)` with `DeviceInfo.Unknown` and `DeviceInfo.Resolve(string?, string?, string?)` (fills a random `DeviceId` when the client sent none, so a session always has an identity to show/revoke).

- [ ] **Step 1: `DeviceInfo.cs`**

```csharp
namespace AioKin.Services.Auth.Token;

/// <summary>
/// Thiet bi phat token. DeviceId co the null tu client cu chua cap nhat — Resolve() sinh
/// mot id tam thoi de phien van track duoc rieng, chi khong hien thi ten/nen tang.
/// </summary>
public sealed record DeviceInfo(string? DeviceId, string? DeviceName, string? Platform)
{
    public static readonly DeviceInfo Unknown = new(null, null, null);

    public static DeviceInfo Resolve(string? deviceId, string? deviceName, string? platform)
        => new(string.IsNullOrWhiteSpace(deviceId) ? $"unknown-{Guid.NewGuid():N}" : deviceId, deviceName, platform);
}
```

- [ ] **Step 2: Add device fields to `AccessTokenSession.cs`**

```csharp
    public string? DeviceId { get; init; }
    public string? DeviceName { get; init; }
    public string? Platform { get; init; }
```

(added after the existing `Role` property, before `IssuedAtUnix`).

- [ ] **Step 3: Update `IAccessTokenService.cs` signatures**

```csharp
    Task<string> CreateForCustomerAsync(UserDb user, DeviceInfo device);
    Task<string> CreateForStaffAsync(StaffDb staff, string roleName, DeviceInfo device);
```

- [ ] **Step 4: Update `AccessTokenService.cs`**

```csharp
    public Task<string> CreateForCustomerAsync(UserDb user, DeviceInfo device)
    {
        var fullName = $"{user.FirstName} {user.LastName}".Trim();

        return IssueAsync(new AccessTokenSession
        {
            Kind = AccessTokenSubjectKind.Customer,
            Subject = user.UserCode,
            UserUuid = user.UserUUID,
            Username = user.Username,
            Email = user.Email,
            Name = string.IsNullOrWhiteSpace(fullName) ? user.Username : fullName,
            Role = Roles.CUSTOMER,
            DeviceId = device.DeviceId,
            DeviceName = device.DeviceName,
            Platform = device.Platform
        });
    }

    public Task<string> CreateForStaffAsync(StaffDb staff, string roleName, DeviceInfo device)
        => IssueAsync(new AccessTokenSession
        {
            Kind = AccessTokenSubjectKind.Staff,
            Subject = staff.Username,
            StaffId = staff.StaffID,
            Username = staff.Username,
            Email = staff.Email,
            Name = staff.FullName,
            Role = roleName,
            DeviceId = device.DeviceId,
            DeviceName = device.DeviceName,
            Platform = device.Platform
        });
```

`IssueAsync`, `TrackSessionAsync`, `ValidateAsync`, `RevokeAsync`, `RevokeAllForSubjectAsync` are unchanged by this step.

- [ ] **Step 5: Add optional device fields to the request DTOs**

`AioKin/Models/InputModel/Auth/User/LoginRequest.cs` — add to both `LoginRequest` and `RefreshTokenRequest`:

```csharp
    /// <summary>Id thiet bi client tu sinh va giu on dinh (vd UUID trong Keystore/Keychain). Bo trong van dang nhap duoc.</summary>
    public string? DeviceId { get; set; }

    public string? DeviceName { get; set; }

    /// <summary>"android" | "ios" | "web" — chi de hien thi trong /account/sessions.</summary>
    public string? Platform { get; set; }
```

`AioKin/Models/InputModel/Auth/User/OtpRequests.cs` — same three properties added to `VerifyOtpRequest`.

- [ ] **Step 6: Fix every remaining call site to compile**

`AuthController.Login`, `VerifyOtp`, `RefreshToken`: build `var device = DeviceInfo.Resolve(model.DeviceId, model.DeviceName, model.Platform);` right after the existing validation checks, then pass `device` to `CreateForCustomerAsync`/`CreateForStaffAsync` and pass `device.DeviceId` as the third `GenerateAsync` argument (replacing the `null` Task 1 left there for `Login`/`VerifyOtp`/`RefreshToken`).

`AdminAuthService.LoginAsync`, `OAuthService`: pass `DeviceInfo.Unknown` to `CreateForStaffAsync`/`CreateForCustomerAsync` and `DeviceInfo.Unknown.DeviceId` (i.e. `null`) to `GenerateAsync` — these two paths have no device fields to read (admin login DTO, OAuth popup redirect) and adding them is out of scope for this plan.

`AioKin.Tests/Infrastructure/TestUser.cs`: `await tokens.CreateForCustomerAsync(user, DeviceInfo.Unknown);` (add `using AioKin.Services.Auth.Token;` if not already present — it already is, for `IAccessTokenService`).

- [ ] **Step 7: Run tests**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(auth): thread deviceId/deviceName/platform through login, refresh and access sessions"
```

---

### Task 3: `GET /account/sessions`

**Files:**
- Create: `AioKin/Models/ViewModel/Auth/User/SessionResponse.cs`
- Modify: `AioKin/Services/Auth/Token/IAccessTokenService.cs` (add `ListSessionsAsync`)
- Modify: `AioKin/Services/Auth/Token/AccessTokenService.cs` (implement it)
- Modify: `AioKin/Controllers/Account/AccountController.cs`
- Create: `AioKin.Tests/Auth/SessionManagementTests.cs`

**Interfaces:**
- Consumes: `AioKinClaims.GetSessionToken()` (from the opaque-access-tokens plan) to compute the caller's own `isCurrent` id.
- Produces: `IAccessTokenService.ListSessionsAsync(string subject) : Task<IReadOnlyList<(string Id, AccessTokenSession Session)>>` — `Id` is `sha256(token)[..12]`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using AioKin.Tests.Infrastructure;
using Xunit;

namespace AioKin.Tests.Auth;

[Collection(ApiCollection.Name)]
public class SessionManagementTests
{
    private readonly ApiFixture _fixture;

    public SessionManagementTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetSessions_liet_ke_dung_1_phien_va_danh_dau_isCurrent()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var response = await testUser.Client.GetAsync("/account/sessions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<List<SessionListItem>>>();
        Assert.NotNull(body);
        var sessions = body!.Data!;
        Assert.Single(sessions);
        Assert.True(sessions[0].IsCurrent);
    }
}

// Doi voi test doc JSON: OperationResult.Data la object, nen dung shape rieng cho deserialize
// thay vi ep kieu OperationResult that.
public class OperationResultOf<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
}

public class SessionListItem
{
    public string Id { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? Platform { get; set; }
    public long IssuedAt { get; set; }
    public bool IsCurrent { get; set; }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~SessionManagementTests`
Expected: FAIL — 404, `/account/sessions` does not exist yet.

- [ ] **Step 3: `SessionResponse.cs`**

```csharp
using System.Text.Json.Serialization;

namespace AioKin.Models.ViewModel.Auth.User;

public class SessionResponse
{
    /// <summary>12 ky tu dau cua sha256(token) — khong the dao nguoc ve token that.</summary>
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    public string? Platform { get; set; }

    [JsonPropertyName("issuedAt")]
    public long IssuedAtUnix { get; set; }

    public bool IsCurrent { get; set; }
}
```

- [ ] **Step 4: Add `ListSessionsAsync` to `IAccessTokenService.cs`**

```csharp
    /// <summary>Moi access session dang song cua mot subject, kem id cong khai (12 ky tu dau cua hash).</summary>
    Task<IReadOnlyList<(string Id, AccessTokenSession Session)>> ListSessionsAsync(string subject);
```

- [ ] **Step 5: Implement in `AccessTokenService.cs`**

```csharp
    private const int PublicIdLength = 12;

    public async Task<IReadOnlyList<(string Id, AccessTokenSession Session)>> ListSessionsAsync(string subject)
    {
        var key = RedisKeys.UserAccessSessions(subject);
        var existing = await _redis.GetStringAsync(key) ?? string.Empty;
        var result = new List<(string Id, AccessTokenSession Session)>();

        foreach (var hash in existing.Split(SessionSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var session = await _redis.GetAsync<AccessTokenSession>(RedisKeys.AccessSession(hash));
            if (session is not null)
                result.Add((hash[..PublicIdLength], session));
        }

        return result;
    }
```

- [ ] **Step 6: Add the endpoint to `AccountController.cs`**

Add `IAccessTokenService accessTokenService` to the constructor (store as `_accessTokenService`), then:

```csharp
    /// <summary>Danh sach thiet bi dang dang nhap cua tai khoan nay.</summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions()
    {
        var userCode = User.GetUserCode();
        if (string.IsNullOrEmpty(userCode))
            return Unauthorized(OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        var currentToken = User.GetSessionToken();
        var currentId = string.IsNullOrEmpty(currentToken) ? null : TokenHash.Sha256Hex(currentToken)[..12];

        var sessions = await _accessTokenService.ListSessionsAsync(userCode);

        var response = sessions.Select(s => new SessionResponse
        {
            Id = s.Id,
            DeviceName = s.Session.DeviceName,
            Platform = s.Session.Platform,
            IssuedAtUnix = s.Session.IssuedAtUnix,
            IsCurrent = s.Id == currentId
        }).ToList();

        return Ok(OperationResult.Ok(data: response));
    }
```

Add `using AioKin.Services.Auth.Token;` for `IAccessTokenService`, `using AioKin.Common;` is already present for `TokenHash`.

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~SessionManagementTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(auth): add GET /account/sessions"
```

---

### Task 4: `DELETE /account/sessions/{id}`

**Files:**
- Modify: `AioKin/Services/Auth/Token/IAccessTokenService.cs` (add `RevokeByIdAsync`)
- Modify: `AioKin/Services/Auth/Token/AccessTokenService.cs` (implement it)
- Modify: `AioKin/Controllers/Account/AccountController.cs`
- Modify: `AioKin.Tests/Auth/SessionManagementTests.cs`

**Interfaces:**
- Consumes: `IRefreshTokenService.RevokeAllForDeviceAsync` (Task 1).
- Produces: `IAccessTokenService.RevokeByIdAsync(string subject, string id) : Task<bool>` — `true` if a session matching that id and subject was found and revoked; `false` means "not found for this subject" (controller maps that to 404, never distinguishing "wrong owner" from "doesn't exist").

- [ ] **Step 1: Write the failing test (append to `SessionManagementTests.cs`)**

```csharp
    [Fact]
    public async Task DeleteSession_thu_hoi_access_token_va_khong_anh_huong_nguoi_khac()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var stranger = await TestUser.CreateAsync(_fixture);

        var listResponse = await owner.Client.GetFromJsonAsync<OperationResultOf<List<SessionListItem>>>("/account/sessions");
        var sessionId = listResponse!.Data![0].Id;

        // Nguoi la khong xoa duoc session cua owner — phai tra 404, khong duoc lam gi ca.
        var forbidden = await stranger.Client.DeleteAsync($"/account/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        var stillWorks = await owner.Client.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);

        var deleted = await owner.Client.DeleteAsync($"/account/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        var afterDelete = await owner.Client.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterDelete.StatusCode);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~SessionManagementTests`
Expected: FAIL — `DELETE /account/sessions/{id}` does not exist yet (404 for the wrong reason — route not found rather than ownership check).

- [ ] **Step 3: Add `RevokeByIdAsync` to `IAccessTokenService.cs`**

```csharp
    /// <summary>Thu hoi 1 session bang id cong khai. Tra false neu id khong thuoc ve subject nay (bao gom "khong ton tai").</summary>
    Task<bool> RevokeByIdAsync(string subject, string id);
```

- [ ] **Step 4: Implement in `AccessTokenService.cs`**

```csharp
    public async Task<bool> RevokeByIdAsync(string subject, string id)
    {
        var key = RedisKeys.UserAccessSessions(subject);
        var existing = await _redis.GetStringAsync(key) ?? string.Empty;
        var hashes = existing.Split(SessionSeparator, StringSplitOptions.RemoveEmptyEntries);

        var match = hashes.FirstOrDefault(h => h.StartsWith(id, StringComparison.Ordinal));
        if (match is null)
            return false;

        await _redis.DeleteAsync(RedisKeys.AccessSession(match));

        var remaining = hashes.Where(h => h != match);
        await _redis.SetStringAsync(key, string.Join(SessionSeparator, remaining), TimeSpan.FromSeconds(AccessTokenLifetimeSeconds));

        return true;
    }
```

- [ ] **Step 5: Add the endpoint to `AccountController.cs`**

Add `IRefreshTokenService` is already injected in this controller. Add:

```csharp
    /// <summary>Dang xuat 1 thiet bi cu the: thu hoi access session va refresh token cung thiet bi do.</summary>
    [HttpDelete("sessions/{id}")]
    public async Task<IActionResult> DeleteSession(string id)
    {
        var userCode = User.GetUserCode();
        if (string.IsNullOrEmpty(userCode))
            return Unauthorized(OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        // Doc deviceId TRUOC khi xoa: sau khi RevokeByIdAsync xoa key, thong tin nay se mat.
        var sessions = await _accessTokenService.ListSessionsAsync(userCode);
        var target = sessions.FirstOrDefault(s => s.Id == id);

        var revoked = await _accessTokenService.RevokeByIdAsync(userCode, id);
        if (!revoked)
            return NotFound(OperationResult.Fail("NotFound", "Khong tim thay phien dang nhap nay."));

        if (target.Session?.DeviceId is { } deviceId)
            await _refreshTokenService.RevokeAllForDeviceAsync(userCode, deviceId);

        return Ok(OperationResult.Ok("Da dang xuat thiet bi."));
    }
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~SessionManagementTests`
Expected: PASS.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 8: Run `detect_changes()`**

`detect_changes({scope: "compare", base_ref: "feat/opaque-access-tokens"})` — confirm only this plan's File Structure files changed.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(auth): add DELETE /account/sessions/{id}, revoking the matching refresh token too"
```

---

## After this plan

Access and refresh tokens both carry a device identity, and a customer can see and selectively kill their own sessions. `docs/superpowers/plans/2026-09-25-biometric-device-login.md` builds on this: registering a biometric credential is naturally keyed by the same `deviceId`, and a successful biometric login issues tokens through the exact same `IAccessTokenService`/`IRefreshTokenService` calls this plan finished wiring up.
