# Opaque Access Tokens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the JWT access token (`JwtTokenService`, `AddJwtBearer`, `JwtBlacklistMiddleware`) with an opaque, server-side-tracked access token, so revocation is an immediate Redis key delete instead of a TTL-shadowing blacklist.

**Architecture:** A new `IAccessTokenService` mints a random opaque string and stores a JSON `AccessTokenSession` (everything the old JWT claims carried) in Redis under `sha256(token)`. A custom `AuthenticationHandler` replaces `AddJwtBearer`: it looks the token up in Redis and builds the `ClaimsPrincipal` by hand instead of verifying a signature. `JwtBlacklistMiddleware` is deleted outright — "the session still exists in Redis" *is* the validity check, so there is nothing left to blacklist against. The refresh token is untouched in this plan (it is already opaque); device tagging and session listing come in the next plan.

**Tech Stack:** .NET 9, ASP.NET Core custom `AuthenticationHandler`, `IRedisService` (existing 3-tier abstraction), xUnit + `Microsoft.AspNetCore.Mvc.Testing` against real Postgres (existing harness), `MemoryCacheRedisService` for isolated unit tests that don't need the full API harness.

**Spec:** `docs/auth-opaque-tokens-biometric.md` — section 2 (current state), section 3 (Phase A target), section 6 (API table — this plan changes no request/response shape), section 7 (locked decisions, row "Thu tu trien khai").

## Global Constraints

- **Comments and docs are Vietnamese without diacritics** ("khong dau"), matching the rest of the repo.
- **Identity always comes from the token.** No endpoint in this plan accepts a caller identity as a body/route parameter.
- **`Jwt:ExpiryMinutes` config key is kept as-is.** Do not rename it or require a new environment variable — `AccessTokenService` reads the same key `JwtConfiguration.ResolveAccessTokenMinutes` already reads. `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience` become unread by the end of this plan; do not delete them from `appsettings*.json` or deployment docs (Task 6 only stops the code from reading them).
- **Run `impact({target, direction: "upstream"})` before editing any of the following symbols**, per `CLAUDE.md`: `IJwtTokenService`, `JwtTokenService`, `JwtBlacklistMiddleware`, `Program.cs` auth block. Blast radius already gathered for this plan (see Review Focus and Task 4) — re-run it yourself if you have doubts before touching a file this plan doesn't list.
- **Run `detect_changes()` before the final commit of this plan** and confirm only the files this plan lists changed.

## Review Focus

- **A request with no `Authorization` header** must still return 401 on a protected endpoint (`AuthenticateResult.NoResult()` path in the handler) — not a 500 from a null-reference on a missing header.
- **A garbage/forged Bearer token** (right shape, wrong value) must return 401, not throw — `ValidateAsync` returns `null` on Redis miss, handler must map that to `AuthenticateResult.Fail`, never let an exception escape.
- **`[Authorize(Roles = ...)]` must keep working unchanged** — the new handler must populate `ClaimTypes.Role` as a claim exactly like `JwtTokenService` did, or every `[Authorize(Roles = Roles.SUPERADMIN)]` etc. in the codebase silently stops working.
- **Staff tokens and Customer tokens carry different claims** (`StaffId` vs `UserCode`/`UserUuid`) — a test must cover both kinds, not just Customer, since `AdminAuthController`/`AdminAuthService` depend on `GetStaffId()`.
- **Swagger's "Bearer" security scheme must still work for manual testing** — the scheme name registered in `AddScheme` must stay `"Bearer"` so the existing `AddSecurityDefinition("Bearer", ...)` in `Program.cs` still matches what `[Authorize]` expects.

---

## Prerequisites

Postgres and Redis running locally (Redis optional — falls back to `MemoryCacheRedisService`):

```bash
docker run -d --name aiokin-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16-alpine
docker run -d --name aiokin-rd -p 6379:6379 redis:7-alpine
```

Branch, per `docs/git-flow.md`:

```bash
git checkout feat/family-core
git pull
git checkout -b feat/opaque-access-tokens
```

---

## File Structure

**New:**

| File | Responsibility |
|---|---|
| `AioKin/Services/Auth/Token/AccessTokenSession.cs` | The record stored in Redis per access token — everything needed to rebuild a `ClaimsPrincipal` |
| `AioKin/Services/Auth/Token/IAccessTokenService.cs` | Contract: issue, validate, revoke |
| `AioKin/Services/Auth/Token/AccessTokenService.cs` | Redis-backed implementation |
| `AioKin/Middleware/OpaqueAccessTokenAuthenticationHandler.cs` | Replaces `AddJwtBearer` — builds `ClaimsPrincipal` from a validated session |
| `AioKin.Tests/Auth/AccessTokenServiceTests.cs` | Unit tests against `MemoryCacheRedisService`, no Postgres needed |
| `AioKin.Tests/Auth/AccessTokenAuthenticationTests.cs` | Integration tests through the real HTTP pipeline (`ApiFixture`) |

**Modified:**

| File | Change |
|---|---|
| `AioKin/Common/AioKinClaims.cs` | Add `SessionToken` claim name + `GetSessionToken()` extension |
| `AioKin/Controllers/Auth/AuthController.cs` | Swap `IJwtTokenService` → `IAccessTokenService`; `Logout`/`LogoutAll` revoke via the new service instead of blacklisting a `jti` |
| `AioKin/Services/Auth/Admin/AdminAuthService.cs` | Same swap for staff login + the two `RevokeAllAsync` call sites (password update, SuperAdmin recovery) |
| `AioKin/Services/Auth/OAuth/OAuthService.cs` | Same swap for Google/Facebook finalize |
| `AioKin/Controllers/Account/AccountController.cs` | Add `IAccessTokenService`, revoke access sessions alongside the existing refresh-token revoke on password change |
| `AioKin/Controllers/Admin/UserManagementController.cs` | Same, for account lock |
| `AioKin/Services/Auth/StaffManagement/StaffManagementService.cs` | Same, for the two staff-deactivate call sites |
| `AioKin/Common/JwtConfiguration.cs` | Drop `ResolveIssuer`/`ResolveAudiences`/`ResolveAudienceForSigning`/`DefaultIdentifier`; keep only `ResolveAccessTokenMinutes` |
| `AioKin/Program.cs` | Remove `AddJwtBearer` block, remove `app.UseJwtBlacklist()`, register the new scheme, swap DI registration `IJwtTokenService` → `IAccessTokenService` |

**Deleted:**

| File | Why |
|---|---|
| `AioKin/Services/Auth/Token/IJwtTokenService.cs` | Fully replaced |
| `AioKin/Services/Auth/Token/JwtTokenService.cs` | Fully replaced |
| `AioKin/Middleware/JwtBlacklistMiddleware.cs` | No longer needed — revocation is a Redis delete now |
| `AioKin.Tests/Infrastructure/TestUser.cs` (edit, not delete) | Update to use `IAccessTokenService` — see Task 4 |

---

### Task 1: `AccessTokenSession` + `IAccessTokenService` contract

**Files:**
- Create: `AioKin/Services/Auth/Token/AccessTokenSession.cs`
- Create: `AioKin/Services/Auth/Token/IAccessTokenService.cs`

**Interfaces:**
- Produces: `AccessTokenSubjectKind` enum, `AccessTokenSession` record, `IAccessTokenService` with `CreateForCustomerAsync(UserDb)`, `CreateForStaffAsync(StaffDb, string roleName)`, `ValidateAsync(string token)`, `RevokeAsync(string token)`, `RevokeAllForSubjectAsync(string subject)`, `AccessTokenLifetimeSeconds`.

- [ ] **Step 1: Write `AccessTokenSession.cs`**

```csharp
namespace AioKin.Services.Auth.Token;

public enum AccessTokenSubjectKind { Customer, Staff }

/// <summary>
/// Du lieu can de dung lai ClaimsPrincipal tu mot access token opaque, luu trong Redis
/// duoi khoa sha256(token). Thay the hoan toan cho claim ben trong JWT cu — khong thieu
/// truong nao ma JwtTokenService tung ghi.
/// </summary>
public sealed class AccessTokenSession
{
    public required AccessTokenSubjectKind Kind { get; init; }

    /// <summary>Khoa dung cho RevokeAllForSubjectAsync — UserCode (Customer) hoac Username (Staff).</summary>
    public required string Subject { get; init; }

    public Guid? UserUuid { get; init; }
    public int? StaffId { get; init; }
    public required string Username { get; init; }
    public string? Email { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }

    public long IssuedAtUnix { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
```

- [ ] **Step 2: Write `IAccessTokenService.cs`**

```csharp
using StaffDb = AioKin.Data.Entities.Security.Staff;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Services.Auth.Token;

/// <summary>
/// Access token opaque, trang thai nam o server (Redis) thay vi tu chua nhu JWT. Thu hoi
/// la xoa key — co hieu luc ngay, khong can blacklist song song.
/// </summary>
public interface IAccessTokenService
{
    /// <summary>Token cho khach hang — role luon la Customer.</summary>
    Task<string> CreateForCustomerAsync(UserDb user);

    /// <summary>Token cho tai khoan quan tri; <paramref name="roleName"/> lay tu Staff.Role.</summary>
    Task<string> CreateForStaffAsync(StaffDb staff, string roleName);

    /// <summary>Tra ve session neu token con hop le, null neu khong ton tai hoac het han.</summary>
    Task<AccessTokenSession?> ValidateAsync(string token);

    /// <summary>Thu hoi dung mot access token — dung khi logout.</summary>
    Task RevokeAsync(string token);

    /// <summary>Thu hoi moi access token dang song cua mot subject (UserCode hoac Username).</summary>
    Task RevokeAllForSubjectAsync(string subject);

    /// <summary>So giay song cua access token — dung cho truong <c>expires_in</c>.</summary>
    int AccessTokenLifetimeSeconds { get; }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build AioKin/AioKin.csproj`
Expected: succeeds (interfaces only, nothing references them yet).

- [ ] **Step 4: Commit**

```bash
git add AioKin/Services/Auth/Token/AccessTokenSession.cs AioKin/Services/Auth/Token/IAccessTokenService.cs
git commit -m "feat(auth): add opaque access token contracts"
```

---

### Task 2: `AccessTokenService` implementation + unit tests

**Files:**
- Create: `AioKin/Services/Auth/Token/AccessTokenService.cs`
- Create: `AioKin.Tests/Auth/AccessTokenServiceTests.cs`

**Interfaces:**
- Consumes: `IAccessTokenService`, `AccessTokenSession`, `AccessTokenSubjectKind` from Task 1; `IRedisService` (existing); `AioKin.Common.RedisKeys`/`RedisTtl` (existing); `AioKin.Common.Roles` (existing); `AioKin.Common.JwtConfiguration.ResolveAccessTokenMinutes` (existing, unchanged by this task).
- Produces: `AccessTokenService` — the concrete DI registration target for `IAccessTokenService`.

- [ ] **Step 1: Write the failing test**

```csharp
using AioKin.Common;
using AioKin.Data.Entities.Security;
using AioKin.Services.Auth.Token;
using AioKin.Services.Common.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AioKin.Tests.Auth;

/// <summary>
/// MemoryCacheRedisService thay cho Redis that: AccessTokenService khong quan tam no dang
/// noi voi ban cai nao cua IRedisService, nen khong can Postgres/ApiFixture cho cac test nay.
/// </summary>
public class AccessTokenServiceTests
{
    private static AccessTokenService CreateSut(int expiryMinutes = 60)
    {
        var redis = new MemoryCacheRedisService(new MemoryCache(new MemoryCacheOptions()), NullLogger<MemoryCacheRedisService>.Instance);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:ExpiryMinutes"] = expiryMinutes.ToString() })
            .Build();
        return new AccessTokenService(redis, config, NullLogger<AccessTokenService>.Instance);
    }

    private static User NewUser() => new()
    {
        UserCode = $"UC{Guid.NewGuid():N}"[..20],
        Username = $"u{Guid.NewGuid():N}"[..20],
        PasswordHash = "x",
        Email = "vi@example.com",
        FirstName = "Vi",
        LastName = "Nguyen"
    };

    [Fact]
    public async Task CreateForCustomerAsync_roi_ValidateAsync_tra_ve_dung_session()
    {
        var sut = CreateSut();
        var user = NewUser();

        var token = await sut.CreateForCustomerAsync(user);
        var session = await sut.ValidateAsync(token);

        Assert.NotNull(session);
        Assert.Equal(AccessTokenSubjectKind.Customer, session!.Kind);
        Assert.Equal(user.UserCode, session.Subject);
        Assert.Equal(user.UserUUID, session.UserUuid);
        Assert.Equal(Roles.CUSTOMER, session.Role);
        Assert.Null(session.StaffId);
    }

    [Fact]
    public async Task CreateForStaffAsync_gan_dung_StaffId_va_role_duoc_truyen_vao()
    {
        var sut = CreateSut();
        var staff = new Staff
        {
            Username = "staff1",
            Email = "staff1@example.com",
            FullName = "Staff One",
            PasswordHash = "x",
            PasswordSalt = "x",
            StaffCode = "STF001"
        };

        var token = await sut.CreateForStaffAsync(staff, Roles.ADMIN);
        var session = await sut.ValidateAsync(token);

        Assert.NotNull(session);
        Assert.Equal(AccessTokenSubjectKind.Staff, session!.Kind);
        Assert.Equal(staff.StaffID, session.StaffId);
        Assert.Equal(Roles.ADMIN, session.Role);
        Assert.Null(session.UserUuid);
    }

    [Fact]
    public async Task ValidateAsync_tra_null_cho_token_khong_ton_tai()
    {
        var sut = CreateSut();

        var session = await sut.ValidateAsync("token-chua-bao-gio-duoc-phat");

        Assert.Null(session);
    }

    [Fact]
    public async Task RevokeAsync_lam_token_het_hop_le_ngay()
    {
        var sut = CreateSut();
        var token = await sut.CreateForCustomerAsync(NewUser());

        await sut.RevokeAsync(token);
        var session = await sut.ValidateAsync(token);

        Assert.Null(session);
    }

    [Fact]
    public async Task RevokeAllForSubjectAsync_thu_hoi_moi_token_dang_song_cua_subject()
    {
        var sut = CreateSut();
        var user = NewUser();

        var tokenA = await sut.CreateForCustomerAsync(user);
        var tokenB = await sut.CreateForCustomerAsync(user);

        await sut.RevokeAllForSubjectAsync(user.UserCode);

        Assert.Null(await sut.ValidateAsync(tokenA));
        Assert.Null(await sut.ValidateAsync(tokenB));
    }

    [Fact]
    public void AccessTokenLifetimeSeconds_doc_tu_JwtExpiryMinutes()
    {
        var sut = CreateSut(expiryMinutes: 15);

        Assert.Equal(15 * 60, sut.AccessTokenLifetimeSeconds);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~AccessTokenServiceTests`
Expected: FAIL — `AccessTokenService` does not exist yet (compile error).

- [ ] **Step 3: Write the implementation**

```csharp
using System.Security.Cryptography;
using System.Text;
using AioKin.Common;
using AioKin.Services.Common.Cache;
using StaffDb = AioKin.Data.Entities.Security.Staff;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Services.Auth.Token;

public class AccessTokenService : IAccessTokenService
{
    private readonly IRedisService _redis;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccessTokenService> _logger;

    /// <summary>So access token song song toi da cho mot subject — cung nguong voi refresh token.</summary>
    private const int MaxSessionsPerSubject = 10;

    private const char SessionSeparator = '\n';

    public AccessTokenService(IRedisService redis, IConfiguration configuration, ILogger<AccessTokenService> logger)
    {
        _redis = redis;
        _configuration = configuration;
        _logger = logger;
    }

    public int AccessTokenLifetimeSeconds => JwtConfiguration.ResolveAccessTokenMinutes(_configuration) * 60;

    public Task<string> CreateForCustomerAsync(UserDb user)
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
            Role = Roles.CUSTOMER
        });
    }

    public Task<string> CreateForStaffAsync(StaffDb staff, string roleName)
        => IssueAsync(new AccessTokenSession
        {
            Kind = AccessTokenSubjectKind.Staff,
            Subject = staff.Username,
            StaffId = staff.StaffID,
            Username = staff.Username,
            Email = staff.Email,
            Name = staff.FullName,
            Role = roleName
        });

    private async Task<string> IssueAsync(AccessTokenSession session)
    {
        // 32 byte ngau nhien, base64url khong padding — cung cach RefreshTokenService dang sinh.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var hash = HashToken(token);
        var ttl = TimeSpan.FromSeconds(AccessTokenLifetimeSeconds);

        await _redis.SetAsync(RedisKeys.AccessSession(hash), session, ttl);
        await TrackSessionAsync(session.Subject, hash, ttl);

        _logger.LogInformation("Access token issued for subject={Subject}, kind={Kind}", session.Subject, session.Kind);
        return token;
    }

    private async Task TrackSessionAsync(string subject, string hash, TimeSpan ttl)
    {
        var key = RedisKeys.UserAccessSessions(subject);
        var existing = await _redis.GetStringAsync(key) ?? string.Empty;
        var hashes = existing.Split(SessionSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        hashes.Add(hash);

        if (hashes.Count > MaxSessionsPerSubject)
        {
            // Token bi day ra khoi danh sach cung phai bi thu hoi, neu khong no van dung
            // duoc cho toi khi het TTL ma khong con cach nao revoke.
            foreach (var evicted in hashes[..^MaxSessionsPerSubject])
                await _redis.DeleteAsync(RedisKeys.AccessSession(evicted));

            hashes = hashes[^MaxSessionsPerSubject..];
        }

        await _redis.SetStringAsync(key, string.Join(SessionSeparator, hashes), ttl);
    }

    public Task<AccessTokenSession?> ValidateAsync(string token)
        => _redis.GetAsync<AccessTokenSession>(RedisKeys.AccessSession(HashToken(token)));

    public async Task RevokeAsync(string token)
    {
        await _redis.DeleteAsync(RedisKeys.AccessSession(HashToken(token)));
        _logger.LogInformation("Access token revoked");
    }

    public async Task RevokeAllForSubjectAsync(string subject)
    {
        var key = RedisKeys.UserAccessSessions(subject);
        var existing = await _redis.GetStringAsync(key) ?? string.Empty;

        foreach (var hash in existing.Split(SessionSeparator, StringSplitOptions.RemoveEmptyEntries))
            await _redis.DeleteAsync(RedisKeys.AccessSession(hash));

        await _redis.DeleteAsync(key);
        _logger.LogInformation("All access sessions revoked for subject={Subject}", subject);
    }

    /// <summary>
    /// Token that khong bao gio nam trong Redis lam key: mot ban dump/backup Redis khong
    /// duoc de lo session dang song duoi dang dung duoc luon.
    /// </summary>
    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
```

Add to `AioKin/Common/RedisKeys.cs`, inside the existing `─── Auth ───` region, right after `UserRefreshTokens`:

```csharp
    /// <summary>Access token session (sau khi bam sha256) → AccessTokenSession JSON.</summary>
    public static string AccessSession(string tokenHash) => $"auth:session:{tokenHash}";

    /// <summary>Tap hop hash cua access token dang song cua mot subject — dung de revoke tat ca.</summary>
    public static string UserAccessSessions(string subject) => $"auth:user_sessions:{subject}";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~AccessTokenServiceTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add AioKin/Services/Auth/Token/AccessTokenService.cs AioKin/Common/RedisKeys.cs AioKin.Tests/Auth/AccessTokenServiceTests.cs
git commit -m "feat(auth): implement opaque AccessTokenService backed by Redis"
```

---

### Task 3: `OpaqueAccessTokenAuthenticationHandler` + wire into the pipeline

Before touching `Program.cs`, run `impact({target: "JwtBlacklistMiddleware", direction: "upstream", repo: "AioKin"})` and `impact({target: "IJwtTokenService", direction: "upstream", repo: "AioKin"})` yourself and confirm the callers match Task 4/5's file list below — this plan was written from that same query, but the index may have moved since.

**Files:**
- Create: `AioKin/Middleware/OpaqueAccessTokenAuthenticationHandler.cs`
- Modify: `AioKin/Common/AioKinClaims.cs`
- Modify: `AioKin/Program.cs:247-280` (the `─── Xac thuc ───` block), `AioKin/Program.cs:409-413` (pipeline ordering comment + `UseJwtBlacklist()` call)
- Create: `AioKin.Tests/Auth/AccessTokenAuthenticationTests.cs`

**Interfaces:**
- Consumes: `IAccessTokenService.ValidateAsync` (Task 2), `AccessTokenSession`, `AccessTokenSubjectKind`.
- Produces: `OpaqueAccessTokenAuthenticationHandler.SchemeName` (const `"Bearer"`) — Task 4/5 controllers don't need it directly, but `Program.cs` does.

- [ ] **Step 1: Add the claim name + extension to `AioKinClaims.cs`**

Add inside `AioKinClaims`:

```csharp
    /// <summary>Access token goc (khong phai jti) — cho phep Logout revoke dung session nay
    /// ma khong phai parse lai header Authorization.</summary>
    public const string SessionToken = "session_token";
```

Add inside `ClaimsPrincipalExtensions`:

```csharp
    public static string? GetSessionToken(this ClaimsPrincipal principal)
        => principal.FindFirstValue(AioKinClaims.SessionToken);
```

`GetJti()` stays in the file for now — Task 4 removes it once nothing calls it.

- [ ] **Step 2: Write the failing integration test**

```csharp
using System.Net;
using System.Net.Http.Headers;
using AioKin.Tests.Infrastructure;
using Xunit;

namespace AioKin.Tests.Auth;

[Collection(ApiCollection.Name)]
public class AccessTokenAuthenticationTests
{
    private readonly ApiFixture _fixture;

    public AccessTokenAuthenticationTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Khong_co_Authorization_header_thi_tra_401()
    {
        var client = _fixture.CreateClient();

        var response = await client.GetAsync("/account/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bearer_token_gia_thi_tra_401_khong_500()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "khong-ton-tai-trong-redis");

        var response = await client.GetAsync("/account/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Access_token_hop_le_thi_qua_duoc_endpoint_can_Authorize()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var response = await testUser.Client.GetAsync("/account/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

This will not compile yet because `TestUser.CreateAsync` (Task 4) still signs via the old `IJwtTokenService`. Leave it — Task 4 fixes `TestUser.cs` and makes this compile and pass together.

- [ ] **Step 3: Write `OpaqueAccessTokenAuthenticationHandler.cs`**

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using AioKin.Common;
using AioKin.Services.Auth.Token;
using Microsoft.AspNetCore.Authentication;

namespace AioKin.Middleware;

/// <summary>
/// Thay AddJwtBearer: doc opaque access token tu header Authorization, tra cuu session
/// trong Redis qua IAccessTokenService, roi dung ClaimsPrincipal thu cong. Khong con chu ky
/// de verify — "hop le" nghia la "con ton tai va chua het TTL trong Redis", nen
/// JwtBlacklistMiddleware khong con can thiet: thu hoi la xoa key, co hieu luc ngay tai day.
/// </summary>
public class OpaqueAccessTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Giu nguyen ten "Bearer" de AddSecurityDefinition("Bearer", ...) trong Swagger
    /// va moi [Authorize] hien co khong phai doi gi.</summary>
    public const string SchemeName = "Bearer";

    private readonly IAccessTokenService _accessTokenService;

    public OpaqueAccessTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IAccessTokenService accessTokenService)
        : base(options, logger, encoder)
    {
        _accessTokenService = accessTokenService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0)
            return AuthenticateResult.NoResult();

        var session = await _accessTokenService.ValidateAsync(token);
        if (session is null)
            return AuthenticateResult.Fail("Access token khong hop le hoac da het han.");

        var identity = new ClaimsIdentity(BuildClaims(session, token), SchemeName, AioKinClaims.Username, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    private static List<Claim> BuildClaims(AccessTokenSession session, string rawToken)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, session.Role),
            new(AioKinClaims.Username, session.Username),
            new(AioKinClaims.SessionToken, rawToken)
        };

        if (session.Kind == AccessTokenSubjectKind.Customer)
        {
            claims.Add(new Claim(AioKinClaims.UserCode, session.Subject));
            if (session.UserUuid is { } uuid)
                claims.Add(new Claim(ClaimTypes.NameIdentifier, uuid.ToString()));
        }

        if (session.StaffId is { } staffId)
            claims.Add(new Claim(AioKinClaims.StaffId, staffId.ToString()));

        if (!string.IsNullOrWhiteSpace(session.Email))
            claims.Add(new Claim(ClaimTypes.Email, session.Email));

        return claims;
    }
}
```

- [ ] **Step 4: Replace the `AddJwtBearer` block in `Program.cs`**

Replace (currently `AioKin/Program.cs:247-280`):

```csharp
var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
});

authentication.AddJwtBearer(options =>
{
    var key = config["Jwt:Key"]
        ?? throw new InvalidOperationException("Thieu Jwt:Key — khong the validate access token.");

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = JwtConfiguration.ResolveIssuer(config),

        ValidateAudience = true,
        ValidAudiences = JwtConfiguration.ResolveAudiences(config),

        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),

        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,

        RoleClaimType = ClaimTypes.Role,
        NameClaimType = AioKinClaims.Username
    };
});
```

with:

```csharp
var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = OpaqueAccessTokenAuthenticationHandler.SchemeName;
    options.DefaultChallengeScheme = OpaqueAccessTokenAuthenticationHandler.SchemeName;
});

// Opaque access token: xem chu thich trong OpaqueAccessTokenAuthenticationHandler.
authentication.AddScheme<AuthenticationSchemeOptions, OpaqueAccessTokenAuthenticationHandler>(
    OpaqueAccessTokenAuthenticationHandler.SchemeName, _ => { });
```

Remove the now-unused `using` lines at the top of `Program.cs`: `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.IdentityModel.Tokens`, `System.IdentityModel.Tokens.Jwt` (the top-of-file `JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Remove(...)` line and its explanatory comment also go — there is no `JwtSecurityTokenHandler` anymore). Add `using AioKin.Middleware;` if not already present (it already is, for `GlobalExceptionHandlerMiddleware`).

- [ ] **Step 5: Remove the blacklist middleware from the pipeline**

In the `─── Pipeline ───` section, delete this line and simplify the comment above it:

```csharp
app.UseJwtBlacklist();
```

The block becomes:

```csharp
// UseAuthentication truoc UseAuthorization — thu tu bat buoc cua ASP.NET Core.
app.UseAuthentication();
app.UseAuthorization();
```

- [ ] **Step 6: Build**

Run: `dotnet build AioKin/AioKin.csproj`
Expected: succeeds. `JwtBlacklistMiddleware.cs` still exists on disk but is unreferenced — Task 5 deletes the file once `IJwtTokenService` is also gone, to keep this task's diff focused on the pipeline swap.

- [ ] **Step 7: Commit**

```bash
git add AioKin/Middleware/OpaqueAccessTokenAuthenticationHandler.cs AioKin/Common/AioKinClaims.cs AioKin/Program.cs AioKin.Tests/Auth/AccessTokenAuthenticationTests.cs
git commit -m "feat(auth): replace AddJwtBearer with opaque token authentication handler"
```

---

### Task 4: Swap every JWT issuer call site to `IAccessTokenService`

**Files:**
- Modify: `AioKin/Controllers/Auth/AuthController.cs` (constructor + `Login`, `VerifyOtp`, `RefreshToken`, `Logout`, `LogoutAll`)
- Modify: `AioKin/Services/Auth/Admin/AdminAuthService.cs` (constructor + `LoginAsync`)
- Modify: `AioKin/Services/Auth/OAuth/OAuthService.cs` (constructor + the token-issuing block)
- Modify: `AioKin/Program.cs` (DI registration line)
- Modify: `AioKin.Tests/Infrastructure/TestUser.cs`
- Delete: `AioKin/Services/Auth/Token/IJwtTokenService.cs`, `AioKin/Services/Auth/Token/JwtTokenService.cs`

**Interfaces:**
- Consumes: `IAccessTokenService` (Task 1/2).

- [ ] **Step 1: DI registration in `Program.cs`**

Replace:

```csharp
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
```

with:

```csharp
builder.Services.AddScoped<IAccessTokenService, AccessTokenService>();
```

(`using AioKin.Services.Auth.Token;` is already present in `Program.cs`.)

- [ ] **Step 2: `AuthController.cs`**

Change the field/constructor:

```csharp
    private readonly IAccessTokenService _accessTokenService;
```
```csharp
        IAccessTokenService accessTokenService,
```
```csharp
        _accessTokenService = accessTokenService;
```

(rename the parameter and field everywhere `_tokenService` appears in this file — every call becomes `await _accessTokenService.CreateForCustomerAsync(user)` / `await _accessTokenService.CreateForStaffAsync(staff, ...)`, since the new methods are `Task<string>`. In `Login` (around line 133):

```csharp
        return Ok(new TokenResponse
        {
            AccessToken = await _accessTokenService.CreateForCustomerAsync(user),
            RefreshToken = await _refreshTokenService.GenerateAsync(user.UserCode, Roles.CUSTOMER),
            ExpiresIn = _accessTokenService.AccessTokenLifetimeSeconds,
            TokenType = "Bearer",
            Scope = Roles.CUSTOMER
        });
```

Same pattern in `VerifyOtp` (around line 228) and `RefreshToken` (around line 401/412/416).

Replace `Logout` (currently blacklists a `jti`):

```csharp
    /// <summary>Dang xuat: thu hoi access token va (neu co) refresh token cua thiet bi nay.</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request = null)
    {
        var sessionToken = User.GetSessionToken();
        if (!string.IsNullOrEmpty(sessionToken))
            await _accessTokenService.RevokeAsync(sessionToken);

        if (!string.IsNullOrEmpty(request?.RefreshToken))
            await _refreshTokenService.RevokeAsync(request.RefreshToken);

        return Ok(OperationResult.Ok("Dang xuat thanh cong."));
    }
```

Replace `LogoutAll`:

```csharp
    /// <summary>Dang xuat khoi moi thiet bi: thu hoi toan bo access va refresh token cua tai khoan.</summary>
    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll()
    {
        var subject = User.GetUserCode() ?? User.GetUsername();
        if (string.IsNullOrEmpty(subject))
            return Unauthorized(OperationResult.Fail("Unauthorized", "Token thieu thong tin dinh danh."));

        await _accessTokenService.RevokeAllForSubjectAsync(subject);
        await _refreshTokenService.RevokeAllAsync(subject);
        return Ok(OperationResult.Ok("Da dang xuat khoi tat ca thiet bi."));
    }
```

`User.GetJti()` is no longer called anywhere in this file after this step.

- [ ] **Step 3: `AdminAuthService.cs`**

Swap the field/constructor the same way (`IJwtTokenService` → `IAccessTokenService`), and in `LoginAsync`:

```csharp
        return new LoginAdminResponse
        {
            FullName = staff.FullName,
            Username = staff.Username,
            Role = roleName,
            Token = await _accessTokenService.CreateForStaffAsync(staff, roleName),
            RefreshToken = await _refreshTokenService.GenerateAsync(staff.Username, roleName),
            ExpiresIn = _accessTokenService.AccessTokenLifetimeSeconds
        };
```

- [ ] **Step 4: `OAuthService.cs`**

Swap the field/constructor the same way, and in the result-building block:

```csharp
    {
        Success = true,
        Token = await _accessTokenService.CreateForCustomerAsync(user),
        RefreshToken = await _refreshTokenService.GenerateAsync(user.UserCode, Roles.CUSTOMER),
        ExpiresIn = _accessTokenService.AccessTokenLifetimeSeconds,
        UserData = UserMapper.ToLoginResponse(user)
    };
```

(this block is inside an `async` method already, per the existing `await _refreshTokenService.GenerateAsync` call on the next line — only the added `await` on `Token` changes).

- [ ] **Step 5: `TestUser.cs`**

```csharp
using AioKin.Common;
using AioKin.Data.Entities.Security;
using AioKin.Services.Auth.Token;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;

namespace AioKin.Tests.Infrastructure;

public sealed class TestUser
{
    public required HttpClient Client { get; init; }
    public required Guid UserUuid { get; init; }
    public required Guid UserId { get; init; }

    public static async Task<TestUser> CreateAsync(ApiFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var accessToken = await tokens.CreateForCustomerAsync(user);

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return new TestUser { Client = client, UserUuid = user.UserUUID, UserId = user.UserID };
    }
}
```

- [ ] **Step 6: Delete the old JWT files**

```bash
git rm AioKin/Services/Auth/Token/IJwtTokenService.cs AioKin/Services/Auth/Token/JwtTokenService.cs
```

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test`
Expected: PASS, including `AccessTokenAuthenticationTests` from Task 3 (now compiles, since `TestUser` no longer references the deleted `IJwtTokenService`) and every existing `AioKin.Tests/Family/*` test (they go through login/token issuance indirectly via `TestUser`).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(auth): issue opaque access tokens from every login path, remove JwtTokenService"
```

---

### Task 5: Revoke access sessions at every existing `RevokeAllAsync` call site

Today, changing a password or locking an account revokes refresh tokens but the *old JWT access token stays valid until it naturally expires* (up to `Jwt:ExpiryMinutes`, default 60 minutes) — the JWT could not be revoked early. Opaque access tokens close that gap for free, but only at call sites that are updated to ask for it.

**Files:**
- Modify: `AioKin/Controllers/Auth/AuthController.cs:365` (`ResetPassword`)
- Modify: `AioKin/Controllers/Account/AccountController.cs:86` (change-password) — add `IAccessTokenService` to constructor
- Modify: `AioKin/Controllers/Admin/UserManagementController.cs:98` (account lock) — add `IAccessTokenService` to constructor
- Modify: `AioKin/Services/Auth/StaffManagement/StaffManagementService.cs:139,160` (staff status/deactivate) — add `IAccessTokenService` to constructor
- Modify: `AioKin/Services/Auth/Admin/AdminAuthService.cs:178,228` (staff password update, SuperAdmin recovery) — already has `IAccessTokenService` from Task 4
- Test: `AioKin.Tests/Auth/AccessTokenRevocationTests.cs`

**Interfaces:**
- Consumes: `IAccessTokenService.RevokeAllForSubjectAsync(string subject)` (Task 1/2).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using AioKin.Tests.Infrastructure;
using Xunit;

namespace AioKin.Tests.Auth;

[Collection(ApiCollection.Name)]
public class AccessTokenRevocationTests
{
    private readonly ApiFixture _fixture;

    public AccessTokenRevocationTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Doi_mat_khau_lam_access_token_cu_het_hop_le_ngay()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var before = await testUser.Client.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var changePassword = await testUser.Client.PostAsJsonAsync("/account/me/change-password", new
        {
            currentPassword = "x", // TestUser.CreateAsync tao user voi PasswordHash "x" tho, khong phai hash that —
                                     // xem ghi chu duoi Step 2 neu buoc nay khong xac thuc duoc mat khau cu.
            newPassword = "MatKhauMoi123!"
        });

        var after = await testUser.Client.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~AccessTokenRevocationTests`
Expected: FAIL at the `after` assertion (old token still works), OR the `changePassword` call itself fails with `InvalidCredentials` because `TestData.NewUser()` sets a raw, unhashed `PasswordHash = "x"` that `PasswordHelper.VerifyPassword` will reject. If it's the latter, read `AioKin/Controllers/Account/AccountController.cs`'s change-password handler to confirm the expected current-password check, then build the test user with a real hash instead:

```csharp
    PasswordHelper.CreatePasswordHash("OldPassw0rd!", out var hash, out var salt);
    var user = TestData.NewUser();
    user.PasswordHash = hash;
    user.PasswordSalt = salt;
```

and send `currentPassword = "OldPassw0rd!"` in the request. Adjust the test to whichever shape makes the *revocation* assertion (the `after` call) the one that fails — that is the behavior this task adds.

- [ ] **Step 3: Add `IAccessTokenService` and the revoke call at each site**

`AccountController.cs` — constructor gains `IAccessTokenService accessTokenService` (store as `_accessTokenService`), and right after the existing line:

```csharp
        await _refreshTokenService.RevokeAllAsync(user.UserCode);
```

add:

```csharp
        await _accessTokenService.RevokeAllForSubjectAsync(user.UserCode);
```

`UserManagementController.cs` — same pattern after:

```csharp
            await _refreshTokenService.RevokeAllAsync(user.UserCode);
```

add:

```csharp
            await _accessTokenService.RevokeAllForSubjectAsync(user.UserCode);
```

`StaffManagementService.cs` — same pattern after **both** occurrences of:

```csharp
        await _refreshTokenService.RevokeAllAsync(entity.Username);
```

add:

```csharp
        await _accessTokenService.RevokeAllForSubjectAsync(entity.Username);
```

`AdminAuthService.cs` — same pattern after **both** occurrences of:

```csharp
        await _refreshTokenService.RevokeAllAsync(target.Username);
```

add:

```csharp
        await _accessTokenService.RevokeAllForSubjectAsync(target.Username);
```

`AuthController.cs`'s `ResetPassword` — same pattern after:

```csharp
        await _refreshTokenService.RevokeAllAsync(user.UserCode);
```

add:

```csharp
        await _accessTokenService.RevokeAllForSubjectAsync(user.UserCode);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~AccessTokenRevocationTests`
Expected: PASS.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS — this task only adds calls, it does not change any existing return shape.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(auth): revoke access sessions alongside refresh tokens on password change and account lock"
```

---

### Task 6: Delete dead JWT code, clean up `JwtConfiguration`, delete `JwtBlacklistMiddleware.cs`

**Files:**
- Modify: `AioKin/Common/JwtConfiguration.cs`
- Modify: `AioKin/Common/AioKinClaims.cs` (remove dead `GetJti()`)
- Delete: `AioKin/Middleware/JwtBlacklistMiddleware.cs`

**Interfaces:** none — this task removes code, it adds nothing new for later tasks to consume.

- [ ] **Step 1: Confirm nothing else reads the methods being removed**

Run:
```bash
grep -rn "ResolveIssuer\|ResolveAudiences\|ResolveAudienceForSigning\|GetJti" AioKin AioKin.Tests
```
Expected: no hits outside `JwtConfiguration.cs`/`AioKinClaims.cs` themselves (Task 3/4 already removed every caller: the old `AddJwtBearer` block, and `AuthController.Logout`/`LogoutAll`). If this finds a caller, stop and re-check Task 3/4 instead of deleting the method under it.

Remove the now-dead `GetJti()` extension from `AioKin/Common/AioKinClaims.cs` in the same step:

```csharp
    public static string? GetJti(this ClaimsPrincipal principal)
        => principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti);
```

Delete this method entirely — nothing calls it after Task 4.

- [ ] **Step 2: Trim `JwtConfiguration.cs`**

```csharp
namespace AioKin.Common;

/// <summary>Cau hinh lien quan den access token opaque — tuoi tho doc tu Jwt:ExpiryMinutes.</summary>
public static class JwtConfiguration
{
    /// <summary>So phut song cua access token.</summary>
    public static int ResolveAccessTokenMinutes(IConfiguration config)
        => int.TryParse(config["Jwt:ExpiryMinutes"], out var m) && m > 0 ? m : 60;
}
```

- [ ] **Step 3: Delete the blacklist middleware file**

```bash
git rm AioKin/Middleware/JwtBlacklistMiddleware.cs
```

- [ ] **Step 4: Build and run the full suite**

Run: `dotnet build AioKin/AioKin.csproj && dotnet test`
Expected: both succeed.

- [ ] **Step 5: Run `detect_changes()` per `CLAUDE.md`**

Run `detect_changes({scope: "compare", base_ref: "feat/family-core"})` and confirm the affected symbols match this plan's File Structure table (no unexpected file touched).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore(auth): remove dead JWT issuer/audience config and blacklist middleware"
```

---

## After this plan

Access tokens are opaque and instantly revocable; refresh tokens are unchanged (still opaque, still raw-keyed, still carry no device identity). The next plan, `docs/superpowers/plans/2026-09-25-token-session-management.md`, adds `deviceId` end-to-end and the `/account/sessions` list/revoke endpoints. The plan after that, `docs/superpowers/plans/2026-09-25-biometric-device-login.md`, adds the biometric challenge/response login and depends on this plan's `IAccessTokenService`/`IRefreshTokenService` to issue its final token pair.
