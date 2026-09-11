# M0 — Family Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the household ("gia dinh") scope — families, members with roles, and expiring invite codes — plus the single `IFamilyContext` gate that every later milestone calls before touching shared data.

**Architecture:** Three new entities in a `family` Postgres schema, reached through a scoped `IFamilyContext` service that answers exactly one question ("is the caller a member of this family, and with what role?") backed by a 5-minute Redis cache with explicit invalidation. Controllers never query membership themselves. Identity always comes from the JWT, never from a parameter. This milestone also stands up the repository's first test project, because M0 is the last moment where doing so is cheap.

**Tech Stack:** .NET 9, EF Core 9 + Npgsql, EFCore.NamingConventions (snake_case), StackExchange.Redis behind the existing `IRedisService`, xUnit + `Microsoft.AspNetCore.Mvc.Testing` against a real Postgres.

**Spec:** `docs/ke-hoach-mo-rong.md` — section 2 (M0), section 1.1 (conventions), section 1.2 (error codes), section 9.4 (security), section 12 (locked decisions).

## Global Constraints

- **Comments and docs are Vietnamese without diacritics** ("khong dau"), matching the rest of the repo. Spec section 1.1.
- **Every new entity MUST declare `public const string SubjectType`** and be added to `DbSeeder.SeedRolePermissionsAsync`. Omitting this makes `/ability/rules` emit a rule set that never mentions the entity, and the Android app then forbids everything — a silent failure. Spec section 1.1.
- **CASL rule order is semantic.** A later rule overrides an earlier one. No step in this plan may reorder existing rules. Spec section 1.1.
- **Identity always comes from the token.** No endpoint accepts a `userId`, `userUuid`, or `memberUserId` parameter that identifies the caller. Spec section 1.1.
- **`/posts`, `/todos` and `/ability/rules` success paths stay unwrapped.** This plan adds no endpoint to those three routes; every new endpoint wraps `OperationResult` normally. Spec section 1.1.
- **A user may belong to many families.** `FamilyMember` is a join table. Spec section 12, decision 3.
- **Table and column names are snake_case**, produced by `UseSnakeCaseNamingConvention()` in `Program.cs`. Write PascalCase in C#; never hand-write snake_case identifiers except inside raw SQL strings (index filters, computed columns).
- **Entity primary keys are internal.** `User.UserID` (Guid) is internal; `User.UserUUID` (Guid) is the public id carried in the `sub` claim. Foreign keys point at `UserID`; API surfaces and route parameters use `UUID` values. Resolve between them with a JOIN, as `ScheduleService.GetForUserAsync` does — never with two round trips.
- **One migration for this milestone, named `AddFamily`.** Spec section 9.6.

---

## Prerequisites

Postgres and Redis running locally (Redis optional — without it the app falls back to `MemoryCacheRedisService`):

```bash
docker run -d --name aiokin-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16-alpine
docker run -d --name aiokin-rd -p 6379:6379 redis:7-alpine
```

Branch, per `docs/git-flow.md`:

```bash
git checkout main
git pull
git checkout -b feat/family-core
```

---

## File Structure

**New — production:**

| File | Responsibility |
|---|---|
| `AioKin/Data/Entities/Family/Family.cs` | Household row; owner, quota, active flag |
| `AioKin/Data/Entities/Family/FamilyMember.cs` | Join row: which user is in which family, with what in-family role |
| `AioKin/Data/Entities/Family/FamilyInvite.cs` | Expiring, use-capped, revocable join code |
| `AioKin/Data/Entities/Family/FamilyMemberRole.cs` | `Owner \| Adult \| Child` enum |
| `AioKin/Services/Family/IFamilyContext.cs` | The one membership question, plus cache invalidation |
| `AioKin/Services/Family/FamilyContext.cs` | Implementation: token -> membership, Redis-cached |
| `AioKin/Services/Family/FamilyMembership.cs` | Record returned by `ResolveAsync` |
| `AioKin/Services/Family/IFamilyService.cs` | Create, list, invite, join, member management |
| `AioKin/Services/Family/FamilyService.cs` | Implementation |
| `AioKin/Services/Family/InviteCodeGenerator.cs` | Unambiguous random codes |
| `AioKin/Controllers/Family/FamiliesController.cs` | The seven M0 endpoints |
| `AioKin/Models/InputModel/Family/*.cs` | Request bodies |
| `AioKin/Models/ViewModel/Family/*.cs` | Responses |

**New — tests:**

| File | Responsibility |
|---|---|
| `AioKin.Tests/AioKin.Tests.csproj` | Test project (the repository's first) |
| `AioKin.Tests/Infrastructure/ApiFixture.cs` | Per-run throwaway Postgres database + booted app |
| `AioKin.Tests/Infrastructure/ApiCollection.cs` | xUnit collection binding the fixture |
| `AioKin.Tests/Infrastructure/TestUser.cs` | Creates a customer and returns an authenticated `HttpClient` |
| `AioKin.Tests/Family/*.cs` | Test classes, one per task |

**Modified:**

| File | Change |
|---|---|
| `AioKin/Program.cs` | `AddHttpContextAccessor()`, DI for the new services, `public partial class Program` |
| `AioKin/Data/AioKinDbContext.cs` | Three `DbSet`s + `OnModelCreating` configuration |
| `AioKin/Data/DbSeeder.cs` | CASL rules for the new subjects |
| `AioKin/Common/OperationResultHttpExtensions.cs` | `NotAFamilyMember` -> 403 |
| `AioKin/Common/RedisKeys.cs` | Membership key + TTL |
| `AioKin.sln` | Add the test project |
| `.github/workflows/ci.yml` | Make the test step run for real |
| `README.md` | Document the new endpoints and config |

**Deliberately NOT built in M0:** storage quota enforcement (M3 needs it, M0 only stores the number), family-scoped anything else. YAGNI.

---

## Deviation From The Spec, And Why

Spec section 2 gives `Family` an `InviteCode(unique)` column **and** a `FamilyInvite` table. This plan drops `Family.InviteCode` and keeps only `FamilyInvite`.

A column on `Family` is a permanent, never-expiring code with no use cap and no revocation. Spec section 9.4.8 requires that invite codes expire and have a maximum number of uses. The two cannot both be true, and the permanent one is the one that leaks: it ends up in a screenshot in a group chat and stays valid forever. `FamilyInvite` already expresses everything the column would, with an expiry, a cap, and a `RevokedAt`.

---

### Task 1: Test project and a real-Postgres harness

The repository has no test project. CI already provisions Postgres and Redis services and has a test step that currently no-ops. This task makes that step real. Everything after it is TDD; without it, nothing after it is.

**Files:**
- Create: `AioKin.Tests/AioKin.Tests.csproj`
- Create: `AioKin.Tests/Infrastructure/ApiFixture.cs`
- Create: `AioKin.Tests/Infrastructure/ApiCollection.cs`
- Create: `AioKin.Tests/Infrastructure/HarnessTests.cs`
- Modify: `AioKin/Program.cs` (append the partial class declaration)
- Modify: `AioKin.sln`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: nothing.
- Produces: `AioKin.Tests.Infrastructure.ApiFixture` with `HttpClient Client`, `IServiceScope CreateScope()`, `string ConnectionString`; `ApiCollection.Name` (const string `"api"`) for `[Collection(...)]`.

- [ ] **Step 1: Create the test project file**

Create `AioKin.Tests/AioKin.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.9" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AioKin\AioKin.csproj" />
  </ItemGroup>

</Project>
```

No assertion library beyond xUnit's `Assert`. FluentAssertions changed its licence at version 7; adding a paid dependency to write `Should().Be()` is not a trade worth making.

- [ ] **Step 2: Make `Program` reachable from the test project**

`Program.cs` uses top-level statements, so its generated `Program` class is internal and `WebApplicationFactory<Program>` cannot see it. Append to the very end of `AioKin/Program.cs`:

```csharp

/// <summary>
/// Top-level statement sinh ra class Program voi pham vi internal, ma
/// WebApplicationFactory&lt;Program&gt; thi can no public. Khai bao partial nay chi de mo
/// pham vi — khong them thanh vien nao.
/// </summary>
public partial class Program { }
```

- [ ] **Step 3: Write the fixture**

Create `AioKin.Tests/Infrastructure/ApiFixture.cs`:

```csharp
using AioKin.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AioKin.Tests.Infrastructure;

/// <summary>
/// Tao mot database Postgres rieng cho moi lan chay test, khoi dong app that tren do, roi
/// xoa database luc ket thuc.
///
/// Dung Postgres that chu khong phai InMemory provider: migration EF, index co dieu kien
/// (HasFilter) va cot GENERATED chi lo loi tren Postgres that. CI da cap san service
/// postgres:16-alpine nen day khong phai phu thuoc moi.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"aiokin_test_{Guid.NewGuid():N}";
    private WebApplicationFactory<Program>? _factory;

    public HttpClient Client { get; private set; } = default!;

    public string ConnectionString => BuildConnectionString(_databaseName);

    public async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(BuildConnectionString("postgres")))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"""CREATE DATABASE "{_databaseName}" """, admin);
            await create.ExecuteNonQueryAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Development: Redis va Brevo deu duoc phep roi ve ban cai thay the. O
            // Production app se dung khoi dong khi thieu chung — dung o day thi moi test
            // deu do vi mot ly do khong lien quan gi den thu dang test.
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);

            // App tu choi khoi dong neu khoa ngan hon 32 byte. Chuoi nay chi ton tai trong
            // test — no khong ky token nao ra ngoai tien trinh test.
            builder.UseSetting("Jwt:Key", "aiokin-test-signing-key-32-bytes-minimum!!");
            builder.UseSetting("Jwt:Issuer", "aiokin-test");
            builder.UseSetting("Jwt:Audience", "aiokin-test");
        });

        // Tao client la thu that su khoi dong app, va app chay MigrateAsync + DbSeeder luc
        // khoi dong. Sau dong nay, schema va du lieu seed da san sang.
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_factory is not null)
            await _factory.DisposeAsync();

        // Pool con giu ket noi toi database vua roi thi DROP se bao "database is being
        // accessed by other users". Dong pool truoc, va van dung WITH (FORCE) cho chac.
        NpgsqlConnection.ClearAllPools();

        await using var admin = new NpgsqlConnection(BuildConnectionString("postgres"));
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand(
            $"""DROP DATABASE IF EXISTS "{_databaseName}" WITH (FORCE)""", admin);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>Scope de lay DbContext hoac service bat ky ra kiem tra truc tiep.</summary>
    public IServiceScope CreateScope()
        => _factory!.Services.CreateScope();

    /// <summary>DbContext moi trong mot scope moi. Nho dispose scope kem theo.</summary>
    public static AioKinDbContext Db(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<AioKinDbContext>();

    private static string BuildConnectionString(string database)
    {
        var host = Environment.GetEnvironmentVariable("TEST_PG_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("TEST_PG_PORT") ?? "5432";
        var user = Environment.GetEnvironmentVariable("TEST_PG_USER") ?? "postgres";
        var password = Environment.GetEnvironmentVariable("TEST_PG_PASSWORD") ?? "postgres";

        return $"Host={host};Port={port};Database={database};Username={user};Password={password}";
    }
}
```

> If `DATABASE_URL` is set in your shell, `Program.cs` may prefer it over
> `ConnectionStrings:DefaultConnection` and the tests will run against your real database.
> Unset it before running tests.

- [ ] **Step 4: Write the collection definition**

Create `AioKin.Tests/Infrastructure/ApiCollection.cs`:

```csharp
using Xunit;

namespace AioKin.Tests.Infrastructure;

/// <summary>
/// Mot fixture dung chung cho moi test class: khoi dong app va chay migration mat vai giay,
/// lam lai o tung class thi bo test cham den muc khong ai chay no nua.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
```

- [ ] **Step 5: Write the failing harness test**

Create `AioKin.Tests/Infrastructure/HarnessTests.cs`:

```csharp
using AioKin.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Infrastructure;

[Collection(ApiCollection.Name)]
public class HarnessTests
{
    private readonly ApiFixture _fixture;

    public HarnessTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task App_khoi_dong_thi_migration_va_seed_da_chay()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var roleNames = await db.Roles.Select(r => r.RoleName).ToListAsync();

        Assert.Contains(Roles.CUSTOMER, roleNames);
        Assert.Contains(Roles.SUPERADMIN, roleNames);
    }
}
```

- [ ] **Step 6: Add the test project to the solution and run the test**

```bash
dotnet sln AioKin.sln add AioKin.Tests/AioKin.Tests.csproj
dotnet test AioKin.sln -v minimal
```

Expected: PASS. If it fails with `password authentication failed`, Postgres is not running or the env vars above need setting. If it fails with `Cannot find type Program`, Step 2 was skipped.

This test is not written-to-fail-first — it is the harness proving itself. Every task after this one starts with a genuinely failing test.

- [ ] **Step 7: Make the CI test step real**

In `.github/workflows/ci.yml`, replace the `Test (Release)` step (and the comment above it saying no test project exists) with:

```yaml
      - name: Test (Release)
        run: dotnet test AioKin.sln -c Release --no-build --verbosity normal
        env:
          TEST_PG_HOST: localhost
          TEST_PG_PORT: "5432"
          TEST_PG_USER: postgres
          TEST_PG_PASSWORD: postgres
```

- [ ] **Step 8: Commit**

```bash
git add AioKin.Tests AioKin.sln AioKin/Program.cs .github/workflows/ci.yml
git commit -m "test: them project test dau tien voi harness Postgres that

Fixture tao mot database rieng moi lan chay va xoa di luc ket thuc, roi
khoi dong app that tren do — migration va seed vi vay duoc kiem tra chu
khong phai gia dinh. Buoc test trong CI tu no-op thanh chay that."
```

---

### Task 2: Family entities, DbContext wiring, and the `AddFamily` migration

**Files:**
- Create: `AioKin/Data/Entities/Family/FamilyMemberRole.cs`
- Create: `AioKin/Data/Entities/Family/Family.cs`
- Create: `AioKin/Data/Entities/Family/FamilyMember.cs`
- Create: `AioKin/Data/Entities/Family/FamilyInvite.cs`
- Modify: `AioKin/Data/AioKinDbContext.cs`
- Create: `AioKin/Data/Migrations/*_AddFamily.cs` (generated)
- Create: `AioKin.Tests/Infrastructure/TestData.cs`
- Create: `AioKin.Tests/Family/FamilySchemaTests.cs`

**Interfaces:**
- Consumes: `ApiFixture`, `ApiCollection.Name` from Task 1.
- Produces: `AioKin.Data.Entities.Family.Family` (`FamilyID`, `FamilyUUID`, `Name`, `OwnerUserID`, `StorageQuotaBytes`, `IsActive`, `CreatedDate`, `Members`), `FamilyMember` (`FamilyMemberID`, `FamilyID`, `UserID`, `MemberRole`, `DisplayName`, `JoinedDate`, `IsActive`, `Family`, `User`), `FamilyInvite` (`FamilyInviteID`, `FamilyID`, `Code`, `CreatedByUserID`, `ExpiresAt`, `MaxUses`, `UsedCount`, `RevokedAt`, `CreatedDate`), enum `FamilyMemberRole { Owner, Adult, Child }`, and `AioKinDbContext.Families`, `.FamilyMembers`, `.FamilyInvites`.

- [ ] **Step 1: Write the shared test-data helper**

Every test that needs a user needs a *valid* one, and `User` has two `required` members
(`UserCode`, `Username`), both under unique indexes. Building one inline in each test file
means three places to get wrong. Create `AioKin.Tests/Infrastructure/TestData.cs`:

```csharp
using AioKin.Data.Entities.Security;

namespace AioKin.Tests.Infrastructure;

/// <summary>
/// Du lieu mau cho test. Moi ham deu tra ve doi tuong hop le NGAY khi tao — user thieu
/// UserCode thi khong compile, con hai user trung UserCode thi hong o rang buoc unique,
/// va ca hai loi do khong lien quan gi den thu dang duoc kiem tra.
/// </summary>
public static class TestData
{
    /// <summary>
    /// Mot khach hang hop le, moi lan goi mot danh tinh khac. Username va UserCode deu
    /// unique trong database nen ca hai phai ngau nhien.
    /// </summary>
    public static User NewUser()
    {
        var suffix = Guid.NewGuid().ToString("N");

        return new User
        {
            UserCode = $"UC{suffix}"[..20],
            Username = $"u{suffix}"[..20],
            PasswordHash = "x"
        };
    }
}
```

Check `AioKin/Data/Entities/Security/User.cs` before writing this: if `required` members
exist beyond `UserCode` and `Username`, add them here too. The entity is the authority.

- [ ] **Step 2: Write the failing schema test**

Create `AioKin.Tests/Family/FamilySchemaTests.cs`:

```csharp
using AioKin.Data.Entities.Family;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Tests.Family;

[Collection(ApiCollection.Name)]
public class FamilySchemaTests
{
    private readonly ApiFixture _fixture;

    public FamilySchemaTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Mot_nguoi_khong_the_vao_cung_mot_gia_dinh_hai_lan()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        var family = new FamilyDb { Name = "Nha test", OwnerUserID = user.UserID };
        db.Users.Add(user);
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyID = family.FamilyID,
            UserID = user.UserID,
            MemberRole = FamilyMemberRole.Owner
        });
        await db.SaveChangesAsync();

        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyID = family.FamilyID,
            UserID = user.UserID,
            MemberRole = FamilyMemberRole.Adult
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Ma_moi_la_duy_nhat_tren_toan_he_thong()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var owner = TestData.NewUser();
        var a = new FamilyDb { Name = "Nha A", OwnerUserID = owner.UserID };
        var b = new FamilyDb { Name = "Nha B", OwnerUserID = owner.UserID };
        db.Users.Add(owner);
        db.Families.AddRange(a, b);

        var code = $"C{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        db.FamilyInvites.Add(new FamilyInvite
        {
            FamilyID = a.FamilyID,
            Code = code,
            CreatedByUserID = owner.UserID,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            MaxUses = 5
        });
        await db.SaveChangesAsync();

        db.FamilyInvites.Add(new FamilyInvite
        {
            FamilyID = b.FamilyID,
            Code = code,
            CreatedByUserID = owner.UserID,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            MaxUses = 5
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Xoa_gia_dinh_thi_thanh_vien_va_ma_moi_di_theo()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        var family = new FamilyDb { Name = "Nha xoa", OwnerUserID = user.UserID };
        db.Users.Add(user);
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyID = family.FamilyID,
            UserID = user.UserID,
            MemberRole = FamilyMemberRole.Owner
        });
        db.FamilyInvites.Add(new FamilyInvite
        {
            FamilyID = family.FamilyID,
            Code = $"X{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            CreatedByUserID = user.UserID,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            MaxUses = 5
        });
        await db.SaveChangesAsync();

        db.Families.Remove(family);
        await db.SaveChangesAsync();

        // Ca hai nua deu phai kiem tra. Chi kiem tra thanh vien thi cascade cua ma moi khong
        // co test nao phu, va mot lan doi OnDelete thanh Restrict se di qua ma khong ai thay.
        Assert.Empty(await db.FamilyMembers.Where(m => m.FamilyID == family.FamilyID).ToListAsync());
        Assert.Empty(await db.FamilyInvites.Where(i => i.FamilyID == family.FamilyID).ToListAsync());
    }
}
```

> `User` requires whatever properties are marked `required` on that entity. Open
> `AioKin/Data/Entities/Security/User.cs` and supply exactly those; the two shown here
> (`Username`, `PasswordHash`) are the expected minimum, but the entity is the authority.

- [ ] **Step 3: Run the test to verify it fails**

```bash
dotnet test AioKin.sln --filter FullyQualifiedName~FamilySchemaTests
```

Expected: FAIL to compile — `Family`, `FamilyMember`, `FamilyInvite` and the `DbSet`s do not exist.

- [ ] **Step 4: Write the role enum**

Create `AioKin/Data/Entities/Family/FamilyMemberRole.cs`:

```csharp
namespace AioKin.Data.Entities.Family;

/// <summary>
/// Vai tro trong pham vi mot gia dinh. KHONG phai role he thong: role trong token van la
/// <see cref="AioKin.Common.Roles.CUSTOMER"/>. Hai khai niem nay doc lap nhau — mot nguoi
/// la Customer o tang he thong va dong thoi la Owner o nha minh, Child o nha bo me.
/// </summary>
public enum FamilyMemberRole
{
    Owner = 0,
    Adult = 1,
    Child = 2
}
```

- [ ] **Step 5: Write the `Family` entity**

Create `AioKin/Data/Entities/Family/Family.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Family;

/// <summary>
/// Mot ho gia dinh — pham vi chia se cho chat nhom, kho file, so chi tieu va lich chung.
///
/// Khong co cot InviteCode co dinh o day, du ban thiet ke goc co nhac: mot ma vinh vien
/// khong het han va khong gioi han so lan dung se nam lai trong mot anh chup man hinh nao do
/// mai mai. Ma moi song trong <see cref="FamilyInvite"/>, co han su dung va thu hoi duoc.
/// </summary>
[Table("families", Schema = "family")]
public class Family
{
    /// <summary>Chuoi <c>subject</c> trong rule phan quyen. Phai khop hang so cung ten ben app.</summary>
    public const string SubjectType = "Family";

    [Key]
    public Guid FamilyID { get; set; } = Guid.NewGuid();

    /// <summary>Id cong khai — moi route va DTO dung no. Khong lo FamilyID noi bo.</summary>
    public Guid FamilyUUID { get; set; } = Guid.NewGuid();

    [MaxLength(120)]
    public required string Name { get; set; }

    /// <summary>
    /// Chu ho hien tai, tro toi <c>users.user_id</c> noi bo.
    ///
    /// KHONG phai mot rang buoc duy nhat: mot gia dinh co the co nhieu thanh vien mang vai
    /// tro <see cref="FamilyMemberRole.Owner"/>. Bat bien that su la "luon con it nhat mot
    /// thanh vien Owner dang hoat dong", va no duoc giu bang cach DEM so dong Owner chu
    /// khong bang cach so sanh voi cot nay.
    /// </summary>
    public Guid OwnerUserID { get; set; }

    /// <summary>Han muc dung luong kho file, don vi byte. M0 chi luu; M3 moi cuong che.</summary>
    public long StorageQuotaBytes { get; set; } = DefaultQuotaBytes;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public ICollection<FamilyMember> Members { get; set; } = [];

    /// <summary>10 GB — mac dinh o muc 5.5 cua ban ke hoach.</summary>
    public const long DefaultQuotaBytes = 10L * 1024 * 1024 * 1024;
}
```

- [ ] **Step 6: Write the `FamilyMember` and `FamilyInvite` entities**

Create `AioKin/Data/Entities/Family/FamilyMember.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Data.Entities.Family;

/// <summary>
/// Bang noi giua nguoi dung va gia dinh. La bang noi chu khong phai mot cot tren
/// <c>users</c> vi mot nguoi thuoc duoc nhieu gia dinh (quyet dinh 3, muc 12).
/// </summary>
[Table("family_members", Schema = "family")]
public class FamilyMember
{
    /// <summary>Chuoi <c>subject</c> trong rule phan quyen.</summary>
    public const string SubjectType = "FamilyMember";

    [Key]
    public Guid FamilyMemberID { get; set; } = Guid.NewGuid();

    public Guid FamilyID { get; set; }

    [ForeignKey(nameof(FamilyID))]
    public Family? Family { get; set; }

    public Guid UserID { get; set; }

    [ForeignKey(nameof(UserID))]
    public UserDb? User { get; set; }

    public FamilyMemberRole MemberRole { get; set; } = FamilyMemberRole.Adult;

    /// <summary>Ten hien thi trong pham vi gia dinh nay ("Bo", "Me", "Be Na"). Rong = dung ten tai khoan.</summary>
    [MaxLength(120)]
    public string? DisplayName { get; set; }

    public DateTime JoinedDate { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}
```

Create `AioKin/Data/Entities/Family/FamilyInvite.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Family;

/// <summary>
/// Ma moi vao gia dinh. Ba gioi han bat buoc, moi cai chan mot duong lam dung khac nhau:
/// het han theo thoi gian, gioi han so lan dung, va thu hoi duoc bang tay.
/// </summary>
[Table("family_invites", Schema = "family")]
public class FamilyInvite
{
    /// <summary>Chuoi <c>subject</c> trong rule phan quyen.</summary>
    public const string SubjectType = "FamilyInvite";

    [Key]
    public Guid FamilyInviteID { get; set; } = Guid.NewGuid();

    public Guid FamilyID { get; set; }

    [ForeignKey(nameof(FamilyID))]
    public Family? Family { get; set; }

    /// <summary>Duy nhat tren toan he thong — tra cuu chi bang ma, khong kem gia dinh nao.</summary>
    [MaxLength(16)]
    public required string Code { get; set; }

    public Guid CreatedByUserID { get; set; }

    public DateTime ExpiresAt { get; set; }

    public int MaxUses { get; set; }

    public int UsedCount { get; set; }

    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>Con dung duoc khong. Kiem tra ca ba gioi han o mot cho.</summary>
    public bool IsUsable(DateTime nowUtc)
        => RevokedAt is null && ExpiresAt > nowUtc && UsedCount < MaxUses;
}
```

- [ ] **Step 7: Wire the DbContext**

In `AioKin/Data/AioKinDbContext.cs`, add the using and the `DbSet`s next to the existing ones:

```csharp
using AioKin.Data.Entities.Family;
```

```csharp
    public DbSet<Family> Families => Set<Family>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<FamilyInvite> FamilyInvites => Set<FamilyInvite>();
```

Then add to the end of `OnModelCreating`, after the existing `ScheduleItem` block:

```csharp
        modelBuilder.Entity<Family>(entity =>
        {
            entity.HasIndex(f => f.FamilyUUID).IsUnique();
        });

        modelBuilder.Entity<FamilyMember>(entity =>
        {
            // Mot nguoi mot lan trong mot gia dinh. Khong co rang buoc nay thi "vao nhom hai
            // lan" tao ra hai dong voi hai vai tro khac nhau, va cau hoi "vai tro cua nguoi
            // nay la gi" khong con mot dap an.
            entity.HasIndex(m => new { m.FamilyID, m.UserID }).IsUnique();

            // Truy van nong nhat cua M0: "cac gia dinh cua nguoi dang dang nhap".
            entity.HasIndex(m => m.UserID);

            entity.Property(m => m.MemberRole).HasConversion<string>().HasMaxLength(16);

            // Cascade: thanh vien khong ton tai doc lap voi gia dinh.
            entity.HasOne(m => m.Family)
                .WithMany(f => f.Members)
                .HasForeignKey(m => m.FamilyID)
                .OnDelete(DeleteBehavior.Cascade);

            // Cascade theo nguoi dung: xoa tai khoan thi tu cach thanh vien di theo, giong
            // cach ScheduleItem lam. Restrict o day nghia la khong xoa noi mot tai khoan.
            entity.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserID)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FamilyInvite>(entity =>
        {
            // Tra cuu luc join chi co ma trong tay — unique index nay vua la rang buoc vua
            // la duong truy van.
            entity.HasIndex(i => i.Code).IsUnique();

            entity.HasOne(i => i.Family)
                .WithMany()
                .HasForeignKey(i => i.FamilyID)
                .OnDelete(DeleteBehavior.Cascade);
        });
```

`MemberRole` is stored as text, not as its integer value: a human reading `family_members` in psql should see `Owner`, not `0`. Reordering the enum later would then be a data problem instead of a silent, invisible one.

- [ ] **Step 8: Generate the migration**

```bash
dotnet ef migrations add AddFamily --project AioKin/AioKin.csproj --output-dir Data/Migrations
```

Open the generated file and confirm it contains `EnsureSchema(name: "family")`, three `CreateTable` calls, and no `AlterColumn`/`DropColumn` against any existing table. If it touches an existing table, stop — something in Step 7 changed a shared configuration, and the Android app depends on those tables.

- [ ] **Step 9: Run the tests to verify they pass**

```bash
dotnet test AioKin.sln --filter FullyQualifiedName~FamilySchemaTests
```

Expected: PASS, all three.

- [ ] **Step 10: Commit**

```bash
git add AioKin/Data AioKin.Tests/Infrastructure/TestData.cs AioKin.Tests/Family
git commit -m "feat(family): them entity Family, FamilyMember, FamilyInvite

Bang noi FamilyMember cho phep mot nguoi thuoc nhieu gia dinh. Bo cot
InviteCode co dinh tren Family ma ban thiet ke goc co nhac: ma moi phai
het han va co so lan dung toi da (muc 9.4.8), ma mot cot thi khong the."
```

---

### Task 3: CASL subjects, seed rules, and the `NotAFamilyMember` error code

Spec section 1.1 is explicit: a new entity that never reaches `SeedRolePermissionsAsync` makes the app forbid everything, with no error anywhere. This task closes that gap before any endpoint exists to be forbidden.

**Files:**
- Modify: `AioKin/Data/DbSeeder.cs`
- Modify: `AioKin/Common/OperationResultHttpExtensions.cs`
- Create: `AioKin.Tests/Family/PermissionSeedTests.cs`

**Interfaces:**
- Consumes: `Family.SubjectType`, `FamilyMember.SubjectType` from Task 2.
- Produces: error code string `"NotAFamilyMember"` mapping to HTTP 403.

- [ ] **Step 1: Write the failing test**

Create `AioKin.Tests/Family/PermissionSeedTests.cs`:

```csharp
using AioKin.Common;
using AioKin.Data.Entities.Family;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Family;

[Collection(ApiCollection.Name)]
public class PermissionSeedTests
{
    private readonly ApiFixture _fixture;

    public PermissionSeedTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Rule_cua_Customer_co_nhac_den_Family_va_FamilyMember()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var rules = await db.Roles
            .Where(r => r.RoleName == Roles.CUSTOMER)
            .Select(r => r.Permissions)
            .SingleAsync();

        Assert.Contains(Family.SubjectType, rules);
        Assert.Contains(FamilyMember.SubjectType, rules);
        Assert.Contains(FamilyInvite.SubjectType, rules);
    }

    [Fact]
    public async Task Rule_cu_van_giu_nguyen_thu_tu()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var rules = await db.Roles
            .Where(r => r.RoleName == Roles.CUSTOMER)
            .Select(r => r.Permissions)
            .SingleAsync();

        // Thu tu rule la ngu nghia: "doc duoc Kham pha" phai dung TRUOC "khong sua duoc
        // Kham pha", neu khong luat cam bien mat. Test nay chan moi lan sap xep lai.
        var readDiscovery = rules.IndexOf($$"""{"action":"read","subject":"{{DiscoveryItem.SubjectType}}"}""", StringComparison.Ordinal);
        var cannotEditDiscovery = rules.IndexOf("\"inverted\":true", StringComparison.Ordinal);

        Assert.True(readDiscovery >= 0, "Rule doc Kham pha da bien mat.");
        Assert.True(cannotEditDiscovery > readDiscovery, "Rule cam sua phai dung sau rule cho doc.");
    }
}
```

Add `using AioKin.Data.Entities.Core;` for `DiscoveryItem`.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test AioKin.sln --filter FullyQualifiedName~PermissionSeedTests
```

Expected: the first test FAILS (`Assert.Contains() Failure` — the rule string has no `Family`); the second PASSES already.

- [ ] **Step 3: Extend the seeded customer rules**

In `AioKin/Data/DbSeeder.cs`, in `SeedRolePermissionsAsync`, replace the `customerRules` constant with the version below. **Append only — the three existing rules keep their exact order and text.**

```csharp
        const string customerRules = $$"""
            [
              {"action":"read","subject":"{{DiscoveryItem.SubjectType}}"},
              {"action":["create","update","delete"],"subject":"{{DiscoveryItem.SubjectType}}","inverted":true,"reason":"Noi dung Kham pha do ban bien tap quan ly."},
              {"action":"manage","subject":"{{ScheduleItem.SubjectType}}"},
              {"action":["read","create"],"subject":"{{Family.SubjectType}}"},
              {"action":["update","delete"],"subject":"{{Family.SubjectType}}","inverted":true,"reason":"Chi chu ho moi sua duoc thong tin gia dinh."},
              {"action":"read","subject":"{{FamilyMember.SubjectType}}"},
              {"action":["read","create"],"subject":"{{FamilyInvite.SubjectType}}"}
            ]
            """;
```

`FamilyInvite` is in the rule set because it has a `SubjectType`, and muc 1.1 of the spec is
explicit that an entity with a subject but no rule makes the app forbid it silently. The
rule is deliberately permissive: CASL here decides whether the app *draws* the button, and
the rule set is keyed on the system role (`Customer`), which cannot see in-family role at
all. A `Child` therefore passes this rule and is refused by `IFamilyContext` on the server
(Task 7). That is the correct place for the refusal — see the note below.

Add `using AioKin.Data.Entities.Family;` at the top of the file.

These rules drive **UI affordances only** — whether the app draws a button. Authorization is enforced server-side by `IFamilyContext` (Task 5) and by explicit role checks in `FamilyService` (Tasks 6-8). A client that ignores the rule set entirely gains nothing.

- [ ] **Step 4: Add the error code**

In `AioKin/Common/OperationResultHttpExtensions.cs`, in the `403 Forbidden` group of `MapErrorCodeToStatusCode`, add one line after `"AccessDenied"`:

```csharp
        "NotAFamilyMember" => StatusCodes.Status403Forbidden,
```

Only this one code from spec section 1.2 is added now. The other five (`PayloadTooLarge`, `UnsupportedMediaType`, `QuotaExceeded`, `TransferNotAuthorized`, `UpstreamUnavailable`) belong to M3 and M6; an error code no code path can produce is dead code a reviewer cannot verify.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test AioKin.sln --filter FullyQualifiedName~PermissionSeedTests
```

Expected: PASS, both.

- [ ] **Step 6: Document the upgrade hazard for existing databases**

`SeedRolePermissionsAsync` writes only into roles whose `Permissions` column is empty (`[]`), deliberately, so that hand-edits on a live database survive restarts. The consequence: **an already-seeded database will not pick up the new rules.** Fresh test databases do, which is why the test above passes and production would not.

Add this to `README.md` under `## Migration`:

```markdown
### Cap nhat bo rule CASL cho database da seed

`DbSeeder` chi ghi rule vao role dang de rong, de khong xoa mat cong sua bang tay. Vi vay
sau khi them entity moi (vi du `Family` o M0), database **da chay tu truoc** se khong tu
nhan rule moi — `/ability/rules` van phat bo rule cu va app am tham cam tinh nang moi.

Xoa cot de seeder ghi lai o lan khoi dong sau:

```sql
UPDATE security.roles SET permissions = '[]' WHERE role_name = 'Customer';
```

Chi lam khi bo rule cua role do chua bi sua bang tay. Neu da sua, them rule moi vao cuoi
mang bang tay — **cuoi mang**, vi rule dung sau thang rule dung truoc.
```

- [ ] **Step 7: Commit**

```bash
git add AioKin/Data/DbSeeder.cs AioKin/Common/OperationResultHttpExtensions.cs AioKin.Tests/Family README.md
git commit -m "feat(family): them rule CASL cho Family va them ma loi NotAFamilyMember

Rule moi noi vao CUOI mang: thu tu la ngu nghia nen khong duoc chen vao
giua. Kem mot test chan viec sap xep lai rule cu.

DbSeeder chi ghi vao role dang de rong nen database da chay tu truoc se
khong nhan rule moi — ghi cach xu ly vao README."
```

---

### Task 4: Invite code generator

A pure unit, no database. Split from the service because a code generator that produces ambiguous or predictable codes fails in a way integration tests never surface.

**Files:**
- Create: `AioKin/Services/Family/InviteCodeGenerator.cs`
- Create: `AioKin.Tests/Family/InviteCodeGeneratorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class InviteCodeGenerator` with `const int Length = 10`, `const string Alphabet`, and `static string Next()`.

- [ ] **Step 1: Write the failing test**

Create `AioKin.Tests/Family/InviteCodeGeneratorTests.cs`:

```csharp
using AioKin.Services.Family;
using Xunit;

namespace AioKin.Tests.Family;

public class InviteCodeGeneratorTests
{
    [Fact]
    public void Ma_dai_dung_10_ky_tu()
    {
        Assert.Equal(10, InviteCodeGenerator.Next().Length);
    }

    [Fact]
    public void Ma_khong_chua_ky_tu_de_doc_nham()
    {
        // I/1, O/0, U/V doc qua dien thoai hoac chep tu anh chup man hinh la nham. Loai han
        // chung ra re hon nhieu so voi viec ho tro nguoi dung go sai ma.
        var forbidden = new[] { 'I', 'L', 'O', 'U' };

        for (var i = 0; i < 500; i++)
        {
            var code = InviteCodeGenerator.Next();
            Assert.DoesNotContain(code, c => forbidden.Contains(c));
            Assert.All(code, c => Assert.Contains(c, InviteCodeGenerator.Alphabet));
        }
    }

    [Fact]
    public void Sinh_nhieu_ma_thi_khong_trung_nhau()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 5_000; i++)
            Assert.True(codes.Add(InviteCodeGenerator.Next()), "Sinh ra ma trung trong 5000 lan.");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test AioKin.sln --filter FullyQualifiedName~InviteCodeGeneratorTests
```

Expected: FAIL to compile — `InviteCodeGenerator` does not exist.

- [ ] **Step 3: Write the generator**

Create `AioKin/Services/Family/InviteCodeGenerator.cs`:

```csharp
using System.Security.Cryptography;

namespace AioKin.Services.Family;

/// <summary>
/// Sinh ma moi vao gia dinh.
///
/// Bang chu cai la Crockford Base32: bo I, L, O, U. Bon ky tu do la nguon go nham khi doc ma
/// qua dien thoai hoac chep lai tu anh chup man hinh — I lan voi 1, O lan voi 0, U lan voi V.
///
/// Dung <see cref="RandomNumberGenerator"/> chu khong phai <see cref="Random"/>: ma moi la
/// mot thong tin xac thuc — ai doan duoc ma la vao duoc nhom. Random gieo tu dong ho, va
/// hai tien trinh khoi dong cung luc co the sinh ra cung day so.
/// </summary>
public static class InviteCodeGenerator
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public const int Length = 10;

    /// <summary>Mot ma moi. 32^10 kha nang — khong gian du rong de khong phai lo va cham.</summary>
    public static string Next()
    {
        Span<char> buffer = stackalloc char[Length];

        for (var i = 0; i < Length; i++)
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

        return new string(buffer);
    }
}
```

`RandomNumberGenerator.GetInt32` is used rather than taking bytes modulo 32 because the alphabet has exactly 32 entries; modulo would be unbiased here, but the next person to shorten the alphabet would introduce a bias without noticing.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test AioKin.sln --filter FullyQualifiedName~InviteCodeGeneratorTests
```

Expected: PASS, all three.

- [ ] **Step 5: Commit**

```bash
git add AioKin/Services/Family/InviteCodeGenerator.cs AioKin.Tests/Family/InviteCodeGeneratorTests.cs
git commit -m "feat(family): sinh ma moi bang Crockford Base32

Bo I, L, O, U vi doc nham qua dien thoai. Dung RandomNumberGenerator
chu khong phai Random: ai doan duoc ma la vao duoc nhom."
```

---

### Task 5: `IFamilyContext` — the membership gate

The most important file in M0. Every milestone from M2 onward calls this before touching family-scoped data. Spec section 2 and section 9.4.2.

**Files:**
- Create: `AioKin/Services/Family/FamilyMembership.cs`
- Create: `AioKin/Services/Family/IFamilyContext.cs`
- Create: `AioKin/Services/Family/FamilyContext.cs`
- Modify: `AioKin/Common/RedisKeys.cs`
- Modify: `AioKin/Program.cs`
- Create: `AioKin.Tests/Family/FamilyContextTests.cs`

**Interfaces:**
- Consumes: entities from Task 2; `IRedisService` (existing, `Services/Common/Cache`); `ClaimsPrincipalExtensions.GetUserUuid()` (existing, `Common/AioKinClaims.cs`).
- Produces:
  - `sealed record FamilyMembership(Guid FamilyID, Guid FamilyUUID, Guid UserID, Guid UserUUID, FamilyMemberRole Role)` with `bool CanInvite => Role is Owner or Adult;` and `bool IsOwner => Role is Owner;`
  - `interface IFamilyContext { Task<FamilyMembership?> ResolveAsync(Guid familyUuid, CancellationToken ct = default); Task InvalidateAsync(Guid familyUuid, Guid userUuid, CancellationToken ct = default); Task InvalidateFamilyAsync(Guid familyUuid, CancellationToken ct = default); }`
  - `RedisKeys.FamilyMembership(Guid, Guid)`, `RedisKeys.FamilyMembershipPrefix(Guid)`, `RedisTtl.FamilyMembership`

- [ ] **Step 1: Write the failing test**

Create `AioKin.Tests/Family/FamilyContextTests.cs`:

```csharp
using AioKin.Data.Entities.Family;
using AioKin.Data.Entities.Security;
using AioKin.Services.Family;
using AioKin.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Xunit;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Tests.Family;

[Collection(ApiCollection.Name)]
public class FamilyContextTests
{
    private readonly ApiFixture _fixture;

    public FamilyContextTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Dung scope rieng va nhet thang mot ClaimsPrincipal vao IHttpContextAccessor: test nay
    /// kiem tra logic phan giai tu cach thanh vien, khong phai duong HTTP.
    /// </summary>
    private static IFamilyContext ContextFor(IServiceScope scope, Guid userUuid)
    {
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userUuid.ToString())], "test"))
        };

        return scope.ServiceProvider.GetRequiredService<IFamilyContext>();
    }

    private static async Task<(User User, FamilyDb Family)> SeedFamilyAsync(
        IServiceScope scope, FamilyMemberRole role)
    {
        var db = ApiFixture.Db(scope);
        var user = TestData.NewUser();
        var family = new FamilyDb { Name = "Nha ctx", OwnerUserID = user.UserID };

        db.Users.Add(user);
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyID = family.FamilyID,
            UserID = user.UserID,
            MemberRole = role
        });
        await db.SaveChangesAsync();

        return (user, family);
    }

    [Fact]
    public async Task Thanh_vien_thi_phan_giai_ra_dung_vai_tro()
    {
        using var scope = _fixture.CreateScope();
        var (user, family) = await SeedFamilyAsync(scope, FamilyMemberRole.Adult);

        var membership = await ContextFor(scope, user.UserUUID).ResolveAsync(family.FamilyUUID);

        Assert.NotNull(membership);
        Assert.Equal(FamilyMemberRole.Adult, membership!.Role);
        Assert.Equal(family.FamilyID, membership.FamilyID);
        Assert.Equal(user.UserID, membership.UserID);
        Assert.True(membership.CanInvite);
        Assert.False(membership.IsOwner);
    }

    [Fact]
    public async Task Nguoi_ngoai_thi_tra_null()
    {
        using var scope = _fixture.CreateScope();
        var (_, family) = await SeedFamilyAsync(scope, FamilyMemberRole.Owner);

        var db = ApiFixture.Db(scope);
        var outsider = TestData.NewUser();
        db.Users.Add(outsider);
        await db.SaveChangesAsync();

        Assert.Null(await ContextFor(scope, outsider.UserUUID).ResolveAsync(family.FamilyUUID));
    }

    [Fact]
    public async Task Thanh_vien_bi_vo_hieu_hoa_thi_khong_con_la_thanh_vien()
    {
        using var scope = _fixture.CreateScope();
        var (user, family) = await SeedFamilyAsync(scope, FamilyMemberRole.Child);

        var db = ApiFixture.Db(scope);
        var member = db.FamilyMembers.Single(m => m.FamilyID == family.FamilyID && m.UserID == user.UserID);
        member.IsActive = false;
        await db.SaveChangesAsync();

        Assert.Null(await ContextFor(scope, user.UserUUID).ResolveAsync(family.FamilyUUID));
    }

    [Fact]
    public async Task Go_thanh_vien_roi_thi_cache_khong_con_giu_quyen_cu()
    {
        using var scope = _fixture.CreateScope();
        var (user, family) = await SeedFamilyAsync(scope, FamilyMemberRole.Adult);
        var context = ContextFor(scope, user.UserUUID);

        // Lan dau: nap vao cache.
        Assert.NotNull(await context.ResolveAsync(family.FamilyUUID));

        var db = ApiFixture.Db(scope);
        db.FamilyMembers.Remove(
            db.FamilyMembers.Single(m => m.FamilyID == family.FamilyID && m.UserID == user.UserID));
        await db.SaveChangesAsync();

        // Khong xoa cache thi nguoi vua bi go van doc duoc du lieu ca nha them 5 phut nua.
        await context.InvalidateAsync(family.FamilyUUID, user.UserUUID);

        Assert.Null(await context.ResolveAsync(family.FamilyUUID));
    }

    [Fact]
    public async Task Khong_co_token_thi_tra_null_chu_khong_nem_loi()
    {
        using var scope = _fixture.CreateScope();
        var (_, family) = await SeedFamilyAsync(scope, FamilyMemberRole.Owner);

        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();  // khong co claim nao

        var context = scope.ServiceProvider.GetRequiredService<IFamilyContext>();

        Assert.Null(await context.ResolveAsync(family.FamilyUUID));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test AioKin.sln --filter FullyQualifiedName~FamilyContextTests
```

Expected: FAIL to compile — `IFamilyContext` and `FamilyMembership` do not exist.

- [ ] **Step 3: Write the membership record**

Create `AioKin/Services/Family/FamilyMembership.cs`:

```csharp
using AioKin.Data.Entities.Family;

namespace AioKin.Services.Family;

/// <summary>
/// Ket qua cua mot lan kiem tra tu cach thanh vien. Mang ca id noi bo lan id cong khai vi
/// service goi sau do can id noi bo de ghi khoa ngoai, con controller can id cong khai de
/// phat ra JSON.
/// </summary>
/// <param name="FamilyID">Id noi bo cua gia dinh — dung cho khoa ngoai.</param>
/// <param name="FamilyUUID">Id cong khai cua gia dinh.</param>
/// <param name="UserID">Id noi bo cua nguoi goi.</param>
/// <param name="UserUUID">Id cong khai cua nguoi goi, lay tu token.</param>
/// <param name="Role">Vai tro trong pham vi gia dinh nay.</param>
public sealed record FamilyMembership(
    Guid FamilyID,
    Guid FamilyUUID,
    Guid UserID,
    Guid UserUUID,
    FamilyMemberRole Role)
{
    /// <summary>Owner va Adult moi tao duoc ma moi. Child thi khong.</summary>
    public bool CanInvite => Role is FamilyMemberRole.Owner or FamilyMemberRole.Adult;

    public bool IsOwner => Role is FamilyMemberRole.Owner;
}
```

- [ ] **Step 4: Write the interface**

Create `AioKin/Services/Family/IFamilyContext.cs`:

```csharp
namespace AioKin.Services.Family;

/// <summary>
/// Tra loi duy nhat mot cau hoi: nguoi goi request nay co phai thanh vien cua
/// <c>familyUuid</c> khong, va voi vai tro gi.
///
/// MOI service co pham vi gia dinh phai goi <see cref="ResolveAsync"/> truoc khi cham du
/// lieu. Dat kiem tra o mot cho thay vi lap lai o tung controller: bo sot mot cho la ro ri
/// du lieu ca gia dinh, va mot cho bi bo sot thi khong ai nhin ra khi doc code.
/// </summary>
public interface IFamilyContext
{
    /// <summary>
    /// Tu cach thanh vien cua nguoi dang goi, hoac <c>null</c> neu khong phai thanh vien.
    /// Null cung la ket qua khi token thieu claim danh tinh — goi tra ve
    /// <c>OperationResult.Fail("NotAFamilyMember", ...)</c>.
    /// </summary>
    Task<FamilyMembership?> ResolveAsync(Guid familyUuid, CancellationToken cancellationToken = default);

    /// <summary>Xoa cache cua mot nguoi trong mot gia dinh. Goi ngay khi doi vai tro hoac go thanh vien.</summary>
    Task InvalidateAsync(Guid familyUuid, Guid userUuid, CancellationToken cancellationToken = default);

    /// <summary>Xoa cache cua ca gia dinh. Goi khi gia dinh bi vo hieu hoa hoac xoa.</summary>
    Task InvalidateFamilyAsync(Guid familyUuid, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Add the Redis keys**

In `AioKin/Common/RedisKeys.cs`, add a new section before the closing brace of `RedisKeys`:

```csharp
    // ─── Family ───────────────────────────────────────────────────────────────

    /// <summary>Tu cach thanh vien da phan giai. Xoa NGAY khi doi vai tro hoac go thanh vien.</summary>
    public static string FamilyMembership(Guid familyUuid, Guid userUuid)
        => $"family:{familyUuid}:member:{userUuid}";

    /// <summary>Tien to de xoa cache cua ca gia dinh mot lan.</summary>
    public static string FamilyMembershipPrefix(Guid familyUuid)
        => $"family:{familyUuid}:member:";
```

And in `RedisTtl`:

```csharp
    /// <summary>
    /// Ngan co chu dich. Cache nay dung de tiet kiem mot lan JOIN, khong phai de giu lau —
    /// va no la cache cua mot quyet dinh phan quyen.
    /// </summary>
    public static readonly TimeSpan FamilyMembership = TimeSpan.FromMinutes(5);
```

- [ ] **Step 6: Write the implementation**

Create `AioKin/Services/Family/FamilyContext.cs`:

```csharp
using AioKin.Common;
using AioKin.Data;
using AioKin.Data.Entities.Family;
using AioKin.Services.Common.Cache;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Family;

public class FamilyContext : IFamilyContext
{
    private readonly AioKinDbContext _db;
    private readonly IRedisService _redis;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FamilyContext(
        AioKinDbContext db,
        IRedisService redis,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _redis = redis;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<FamilyMembership?> ResolveAsync(
        Guid familyUuid,
        CancellationToken cancellationToken = default)
    {
        // Danh tinh LUON lay tu token. Khong co tham so nao cua ham nay noi nguoi goi la ai.
        var userUuid = _httpContextAccessor.HttpContext?.User.GetUserUuid();
        if (userUuid is null)
            return null;

        var cacheKey = RedisKeys.FamilyMembership(familyUuid, userUuid.Value);

        var cached = await _redis.GetAsync<CachedMembership>(cacheKey);
        if (cached is not null)
            return cached.ToMembership();

        // Mot lan tra database: JOIN qua navigation property thay vi tra bang users truoc.
        var row = await _db.FamilyMembers
            .AsNoTracking()
            .Where(m => m.IsActive
                     && m.User!.UserUUID == userUuid.Value
                     && m.Family!.FamilyUUID == familyUuid
                     && m.Family.IsActive)
            .Select(m => new CachedMembership
            {
                FamilyID = m.FamilyID,
                FamilyUUID = familyUuid,
                UserID = m.UserID,
                UserUUID = userUuid.Value,
                Role = m.MemberRole
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            // Khong cache ket qua am. Nguoi vua duoc moi vao nha se phai doi het TTL moi vao
            // duoc, va ho se bao "app hong" — trong khi cai tiet kiem duoc chi la mot lan
            // truy van index.
            return null;
        }

        await _redis.SetAsync(cacheKey, row, RedisTtl.FamilyMembership);
        return row.ToMembership();
    }

    public Task InvalidateAsync(Guid familyUuid, Guid userUuid, CancellationToken cancellationToken = default)
        => _redis.DeleteAsync(RedisKeys.FamilyMembership(familyUuid, userUuid));

    public Task InvalidateFamilyAsync(Guid familyUuid, CancellationToken cancellationToken = default)
        => _redis.DeleteByPrefixAsync(RedisKeys.FamilyMembershipPrefix(familyUuid));

    /// <summary>
    /// Ban co the serialize duoc cua <see cref="FamilyMembership"/>. Record positional voi
    /// constructor bat buoc thi khong phai bo serializer nao cung dung lai duoc — mot class
    /// co property doc-ghi thi chac chan.
    /// </summary>
    private sealed class CachedMembership
    {
        public Guid FamilyID { get; set; }
        public Guid FamilyUUID { get; set; }
        public Guid UserID { get; set; }
        public Guid UserUUID { get; set; }
        public FamilyMemberRole Role { get; set; }

        public FamilyMembership ToMembership() => new(FamilyID, FamilyUUID, UserID, UserUUID, Role);
    }
}
```

- [ ] **Step 7: Register in DI**

In `AioKin/Program.cs`, next to `builder.Services.AddHttpClient();`:

```csharp
// FamilyContext doc danh tinh nguoi goi tu HttpContext. Khong dang ky dong nay thi container
// nem loi luc khoi dong (ValidateOnBuild), khong phai luc co request that.
builder.Services.AddHttpContextAccessor();
```

And with the other `AddScoped` registrations, after `builder.Services.AddScoped<IScheduleService, ScheduleService>();`:

```csharp
builder.Services.AddScoped<IFamilyContext, FamilyContext>();
```

Add `using AioKin.Services.Family;` to the usings.

- [ ] **Step 8: Run the tests to verify they pass**

```bash
dotnet test AioKin.sln --filter FullyQualifiedName~FamilyContextTests
```

Expected: PASS, all five.

- [ ] **Step 9: Commit**

```bash
git add AioKin/Services/Family AioKin/Common/RedisKeys.cs AioKin/Program.cs AioKin.Tests/Family/FamilyContextTests.cs
git commit -m "feat(family): them IFamilyContext lam cong kiem tra tu cach thanh vien

Mot cho duy nhat tra loi 'nguoi nay co trong nha nay khong, vai tro gi'.
Cache Redis 5 phut, xoa ngay khi doi vai tro hoac go thanh vien — khong
xoa thi nguoi vua bi go van doc duoc du lieu them 5 phut.

Khong cache ket qua am: nguoi vua duoc moi vao phai vao duoc ngay."
```

---

### Task 6: Create a family, list my families

**Files:**
- Create: `AioKin/Models/InputModel/Family/CreateFamilyRequest.cs`
- Create: `AioKin/Models/ViewModel/Family/FamilyResponse.cs`
- Create: `AioKin/Services/Family/IFamilyService.cs`
- Create: `AioKin/Services/Family/FamilyService.cs`
- Create: `AioKin/Controllers/Family/FamiliesController.cs`
- Create: `AioKin.Tests/Infrastructure/TestUser.cs`
- Create: `AioKin.Tests/Family/FamilyEndpointTests.cs`
- Modify: `AioKin/Program.cs`

**Interfaces:**
- Consumes: `IFamilyContext`, `FamilyMembership` (Task 5); entities (Task 2).
- Produces:
  - `IFamilyService.CreateAsync(Guid callerUserUuid, CreateFamilyRequest request, CancellationToken ct)` -> `Task<OperationResult>` carrying `FamilyResponse` in `Data`
  - `IFamilyService.GetMineAsync(Guid callerUserUuid, CancellationToken ct)` -> `Task<IReadOnlyList<FamilyResponse>>`
  - `FamilyResponse` with `FamilyUuid`, `Name`, `MyRole` (string), `MemberCount` (int), `StorageQuotaBytes` (long), `CreatedAtMillis` (long)
  - `TestUser.RegisterAsync(ApiFixture)` -> `Task<TestUser>` with `HttpClient Client`, `Guid UserUuid`

- [ ] **Step 1: Write the test helper that produces an authenticated client**

Create `AioKin.Tests/Infrastructure/TestUser.cs`:

```csharp
using AioKin.Common;
using AioKin.Data.Entities.Security;
using AioKin.Services.Auth.Token;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;

namespace AioKin.Tests.Infrastructure;

/// <summary>
/// Mot khach hang da dang nhap, kem HttpClient da gan token.
///
/// Tao user thang trong database va ky token bang chinh IJwtTokenService cua app, thay vi
/// di qua luong dang ky OTP that: luong do can email va Redis, va no khong phai thu dang
/// duoc kiem tra o day.
/// </summary>
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

        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var accessToken = tokens.GenerateAccessToken(user, Roles.CUSTOMER);

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return new TestUser { Client = client, UserUuid = user.UserUUID, UserId = user.UserID };
    }
}
```

> `IJwtTokenService`'s exact method name and signature must be taken from
> `AioKin/Services/Auth/Token/IJwtTokenService.cs`. Adjust the `GenerateAccessToken` call to
> match it — do not change the interface to match this plan.

Add to `ApiFixture`:

```csharp
    /// <summary>HttpClient moi, chua gan token. Dung khi can nhieu danh tinh trong mot test.</summary>
    public HttpClient CreateClient() => _factory!.CreateClient();
```

- [ ] **Step 2: Write the failing endpoint test**

Create `AioKin.Tests/Family/FamilyEndpointTests.cs`:

```csharp
using AioKin.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AioKin.Tests.Family;

[Collection(ApiCollection.Name)]
public class FamilyEndpointTests
{
    private readonly ApiFixture _fixture;

    public FamilyEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Tao_gia_dinh_thi_nguoi_tao_thanh_Owner()
    {
        var user = await TestUser.CreateAsync(_fixture);

        var response = await user.Client.PostAsJsonAsync("/families", new { name = "Nha Phong" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());

        var data = body.GetProperty("data");
        Assert.Equal("Nha Phong", data.GetProperty("name").GetString());
        Assert.Equal("Owner", data.GetProperty("myRole").GetString());
        Assert.Equal(1, data.GetProperty("memberCount").GetInt32());
    }

    [Fact]
    public async Task Danh_sach_chi_tra_ve_gia_dinh_cua_chinh_minh()
    {
        var mine = await TestUser.CreateAsync(_fixture);
        var other = await TestUser.CreateAsync(_fixture);

        await mine.Client.PostAsJsonAsync("/families", new { name = "Nha cua toi" });
        await other.Client.PostAsJsonAsync("/families", new { name = "Nha nguoi khac" });

        var response = await mine.Client.GetAsync("/families/me");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal("Nha cua toi", items[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Ten_rong_thi_bi_tu_choi()
    {
        var user = await TestUser.CreateAsync(_fixture);

        var response = await user.Client.PostAsJsonAsync("/families", new { name = "  " });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Khong_co_token_thi_bi_tu_choi()
    {
        var anonymous = _fixture.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/families", new { name = "Nha la" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

```bash
dotnet test AioKin.sln --filter FullyQualifiedName~FamilyEndpointTests
```

Expected: FAIL — 404 on `/families`, because the controller does not exist.

- [ ] **Step 4: Write the request and response models**

Create `AioKin/Models/InputModel/Family/CreateFamilyRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Family;

public class CreateFamilyRequest
{
    /// <summary>Ten hien thi cua gia dinh. Khong duoc de rong.</summary>
    [Required(AllowEmptyStrings = false)]
    [MinLength(1)]
    [MaxLength(120)]
    public required string Name { get; set; }
}
```

`[Required]` alone accepts a string of spaces; `MinLength(1)` does not fix that either. The service trims and re-checks — see Step 6.

Create `AioKin/Models/ViewModel/Family/FamilyResponse.cs`:

```csharp
using AioKin.Data.Entities.Family;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Models.ViewModel.Family;

/// <summary>
/// Mot gia dinh nhin tu phia mot thanh vien cu the — <see cref="MyRole"/> la vai tro cua
/// nguoi dang goi, khong phai mot thuoc tinh cua gia dinh.
/// </summary>
public class FamilyResponse
{
    public Guid FamilyUuid { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Vai tro cua nguoi goi: Owner, Adult hoac Child.</summary>
    public string MyRole { get; set; } = string.Empty;

    public int MemberCount { get; set; }

    public long StorageQuotaBytes { get; set; }

    /// <summary>Epoch millis UTC — dung quy uoc thoi gian cua <c>ScheduleItemResponse</c>.</summary>
    public long CreatedAtMillis { get; set; }

    public static FamilyResponse From(FamilyDb family, FamilyMemberRole myRole, int memberCount) => new()
    {
        FamilyUuid = family.FamilyUUID,
        Name = family.Name,
        MyRole = myRole.ToString(),
        MemberCount = memberCount,
        StorageQuotaBytes = family.StorageQuotaBytes,
        CreatedAtMillis = new DateTimeOffset(
            DateTime.SpecifyKind(family.CreatedDate, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
    };
}
```

- [ ] **Step 5: Write the service interface**

Create `AioKin/Services/Family/IFamilyService.cs`:

```csharp
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Family;
using AioKin.Models.ViewModel.Family;

namespace AioKin.Services.Family;

public interface IFamilyService
{
    /// <summary>Tao gia dinh moi. Nguoi tao thanh Owner trong cung mot transaction.</summary>
    Task<OperationResult> CreateAsync(
        Guid callerUserUuid,
        CreateFamilyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Cac gia dinh nguoi goi dang thuoc ve, moi tao truoc.</summary>
    Task<IReadOnlyList<FamilyResponse>> GetMineAsync(
        Guid callerUserUuid,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: Write the service implementation**

Create `AioKin/Services/Family/FamilyService.cs`:

```csharp
using AioKin.Data;
using AioKin.Data.Entities.Family;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Family;
using AioKin.Models.ViewModel.Family;
using Microsoft.EntityFrameworkCore;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Services.Family;

public class FamilyService : IFamilyService
{
    private readonly AioKinDbContext _db;

    public FamilyService(AioKinDbContext db)
    {
        _db = db;
    }

    public async Task<OperationResult> CreateAsync(
        Guid callerUserUuid,
        CreateFamilyRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
            return OperationResult.Fail("ValidationError", "Ten gia dinh khong duoc de rong.");

        var userId = await _db.Users
            .Where(u => u.UserUUID == callerUserUuid)
            .Select(u => u.UserID)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId == Guid.Empty)
            return OperationResult.Fail("UserNotFound", "Khong tim thay tai khoan.");

        var family = new FamilyDb { Name = name, OwnerUserID = userId };

        // Gia dinh va dong Owner phai cung song hoac cung khong. Mot gia dinh khong co thanh
        // vien nao la mot dong ma khong ai — ke ca nguoi vua tao — cham toi duoc nua.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Families.Add(family);
        _db.FamilyMembers.Add(new FamilyMember
        {
            FamilyID = family.FamilyID,
            UserID = userId,
            MemberRole = FamilyMemberRole.Owner
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OperationResult.Ok("Da tao gia dinh.", FamilyResponse.From(family, FamilyMemberRole.Owner, 1));
    }

    public async Task<IReadOnlyList<FamilyResponse>> GetMineAsync(
        Guid callerUserUuid,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.FamilyMembers
            .AsNoTracking()
            .Where(m => m.IsActive && m.User!.UserUUID == callerUserUuid && m.Family!.IsActive)
            .OrderByDescending(m => m.Family!.CreatedDate)
            .Select(m => new
            {
                Family = m.Family!,
                m.MemberRole,
                // Dem trong cung mot lan tra database. Tra ve roi dem trong bo nho nghia la
                // keo toan bo thanh vien cua moi gia dinh ve app chi de lay mot con so.
                MemberCount = m.Family!.Members.Count(x => x.IsActive)
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => FamilyResponse.From(r.Family, r.MemberRole, r.MemberCount))];
    }
}
```

- [ ] **Step 7: Write the controller**

Create `AioKin/Controllers/Family/FamiliesController.cs`:

```csharp
using AioKin.Common;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Family;
using AioKin.Models.ViewModel.Family;
using AioKin.Services.Family;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Family;

/// <summary>
/// Ho gia dinh — pham vi chia se ma chat nhom, kho file va so chi tieu deu dua tren.
///
/// Danh tinh LUON lay tu token. Khong endpoint nao o day nhan mot tham so noi nguoi goi la
/// ai; tham so <c>uuid</c> tren duong dan la gia dinh dang thao tac, va no luon di qua
/// <see cref="IFamilyContext"/> truoc khi cham du lieu.
/// </summary>
[ApiController]
[Route("families")]
[Produces("application/json")]
[Authorize(Roles = Roles.CUSTOMER)]
public class FamiliesController : ControllerBase
{
    private readonly IFamilyService _familyService;

    public FamiliesController(IFamilyService familyService)
    {
        _familyService = familyService;
    }

    /// <summary>Tao gia dinh moi. Nguoi tao tro thanh Owner.</summary>
    [HttpPost]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateFamilyRequest request,
        CancellationToken cancellationToken)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return this.ToActionResult(
                OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        return this.ToActionResult(
            await _familyService.CreateAsync(userUuid.Value, request, cancellationToken));
    }

    /// <summary>Cac gia dinh nguoi goi dang thuoc ve.</summary>
    [HttpGet("me")]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return this.ToActionResult(
                OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        var families = await _familyService.GetMineAsync(userUuid.Value, cancellationToken);

        return this.ToActionResult(OperationResult.Ok(data: families));
    }
}
```

- [ ] **Step 8: Register the service**

In `AioKin/Program.cs`, beside the `IFamilyContext` registration:

```csharp
builder.Services.AddScoped<IFamilyService, FamilyService>();
```

- [ ] **Step 9: Run the tests to verify they pass**

```bash
dotnet test AioKin.sln --filter FullyQualifiedName~FamilyEndpointTests
```

Expected: PASS, all four.

- [ ] **Step 10: Commit**

```bash
git add AioKin/Models/InputModel/Family AioKin/Models/ViewModel/Family AioKin/Services/Family AioKin/Controllers/Family AioKin/Program.cs AioKin.Tests
git commit -m "feat(family): POST /families va GET /families/me

Tao gia dinh va dong Owner trong cung mot transaction: mot gia dinh
khong co thanh vien nao la dong ma khong ai cham toi duoc nua.

Dem thanh vien trong cung truy van thay vi keo ca bang ve app."
```

---

### Task 7: Invites — create a code, join by code

**Files:**
- Create: `AioKin/Models/InputModel/Family/CreateInviteRequest.cs`
- Create: `AioKin/Models/InputModel/Family/JoinFamilyRequest.cs`
- Create: `AioKin/Models/ViewModel/Family/FamilyInviteResponse.cs`
- Modify: `AioKin/Services/Family/IFamilyService.cs`
- Modify: `AioKin/Services/Family/FamilyService.cs`
- Modify: `AioKin/Controllers/Family/FamiliesController.cs`
- Create: `AioKin.Tests/Family/FamilyInviteTests.cs`

**Interfaces:**
- Consumes: `InviteCodeGenerator.Next()` (Task 4); `IFamilyContext.ResolveAsync` (Task 5); `IFamilyService` (Task 6).
- Produces:
  - `IFamilyService.CreateInviteAsync(Guid familyUuid, CreateInviteRequest request, CancellationToken ct)` -> `Task<OperationResult>`
  - `IFamilyService.JoinAsync(Guid callerUserUuid, JoinFamilyRequest request, CancellationToken ct)` -> `Task<OperationResult>`
  - `FamilyInviteResponse` with `Code`, `ExpiresAtMillis`, `MaxUses`, `UsedCount`

- [ ] **Step 1: Write the failing test**

Create `AioKin.Tests/Family/FamilyInviteTests.cs`:

```csharp
using AioKin.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AioKin.Tests.Family;

[Collection(ApiCollection.Name)]
public class FamilyInviteTests
{
    private readonly ApiFixture _fixture;

    public FamilyInviteTests(ApiFixture fixture) => _fixture = fixture;

    private static async Task<Guid> CreateFamilyAsync(TestUser user, string name)
    {
        var response = await user.Client.PostAsJsonAsync("/families", new { name });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("familyUuid").GetGuid();
    }

    private static async Task<string> CreateInviteAsync(TestUser user, Guid familyUuid, int maxUses = 5)
    {
        var response = await user.Client.PostAsJsonAsync(
            $"/families/{familyUuid}/invites", new { maxUses, expiresInHours = 24 });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Vao_nhom_bang_ma_hop_le_thi_thanh_Adult()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var joiner = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha moi");
        var code = await CreateInviteAsync(owner, familyUuid);

        var response = await joiner.Client.PostAsJsonAsync("/families/join", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mine = await joiner.Client.GetFromJsonAsync<JsonElement>("/families/me");
        var items = mine.GetProperty("data").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("Adult", items[0].GetProperty("myRole").GetString());
        Assert.Equal(2, items[0].GetProperty("memberCount").GetInt32());
    }

    [Fact]
    public async Task Nguoi_ngoai_khong_tao_duoc_ma_moi()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var outsider = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha kin");

        var response = await outsider.Client.PostAsJsonAsync(
            $"/families/{familyUuid}/invites", new { maxUses = 5, expiresInHours = 24 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NotAFamilyMember", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Ma_dung_het_so_lan_thi_khong_dung_duoc_nua()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var first = await TestUser.CreateAsync(_fixture);
        var second = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha mot suat");
        var code = await CreateInviteAsync(owner, familyUuid, maxUses: 1);

        Assert.Equal(HttpStatusCode.OK,
            (await first.Client.PostAsJsonAsync("/families/join", new { code })).StatusCode);

        var response = await second.Client.PostAsJsonAsync("/families/join", new { code });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Vao_lai_nhom_da_o_trong_do_thi_khong_ton_them_luot()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var joiner = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha vao hai lan");
        var code = await CreateInviteAsync(owner, familyUuid, maxUses: 2);

        await joiner.Client.PostAsJsonAsync("/families/join", new { code });
        var response = await joiner.Client.PostAsJsonAsync("/families/join", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mine = await joiner.Client.GetFromJsonAsync<JsonElement>("/families/me");
        Assert.Single(mine.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Ma_khong_ton_tai_thi_tra_404()
    {
        var user = await TestUser.CreateAsync(_fixture);

        var response = await user.Client.PostAsJsonAsync("/families/join", new { code = "ZZZZZZZZZZ" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test AioKin.sln --filter FullyQualifiedName~FamilyInviteTests
```

Expected: FAIL — 404 on the invite routes.

- [ ] **Step 3: Write the request and response models**

Create `AioKin/Models/InputModel/Family/CreateInviteRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Family;

public class CreateInviteRequest
{
    /// <summary>So lan ma nay dung duoc. Toi da 50 — mot ma dung duoc vo han la mot ma vinh vien.</summary>
    [Range(1, 50)]
    public int MaxUses { get; set; } = 5;

    /// <summary>Han su dung, tinh bang gio. Toi da 30 ngay.</summary>
    [Range(1, 720)]
    public int ExpiresInHours { get; set; } = 24;
}
```

Create `AioKin/Models/InputModel/Family/JoinFamilyRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Family;

public class JoinFamilyRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(16)]
    public required string Code { get; set; }
}
```

Create `AioKin/Models/ViewModel/Family/FamilyInviteResponse.cs`:

```csharp
using AioKin.Data.Entities.Family;

namespace AioKin.Models.ViewModel.Family;

public class FamilyInviteResponse
{
    public string Code { get; set; } = string.Empty;

    public long ExpiresAtMillis { get; set; }

    public int MaxUses { get; set; }

    public int UsedCount { get; set; }

    public static FamilyInviteResponse From(FamilyInvite invite) => new()
    {
        Code = invite.Code,
        ExpiresAtMillis = new DateTimeOffset(
            DateTime.SpecifyKind(invite.ExpiresAt, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
        MaxUses = invite.MaxUses,
        UsedCount = invite.UsedCount
    };
}
```

- [ ] **Step 4: Extend the service interface**

Add to `AioKin/Services/Family/IFamilyService.cs`:

```csharp
    /// <summary>Tao ma moi. Chi Owner va Adult goi duoc — Child thi khong.</summary>
    Task<OperationResult> CreateInviteAsync(
        Guid familyUuid,
        CreateInviteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Vao gia dinh bang ma. Vao lai nhom da o trong do la thanh cong va khong ton luot.</summary>
    Task<OperationResult> JoinAsync(
        Guid callerUserUuid,
        JoinFamilyRequest request,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Implement both methods**

In `AioKin/Services/Family/FamilyService.cs`, inject `IFamilyContext` — change the constructor to:

```csharp
    private readonly AioKinDbContext _db;
    private readonly IFamilyContext _familyContext;

    public FamilyService(AioKinDbContext db, IFamilyContext familyContext)
    {
        _db = db;
        _familyContext = familyContext;
    }
```

Then add the two methods:

```csharp
    public async Task<OperationResult> CreateInviteAsync(
        Guid familyUuid,
        CreateInviteRequest request,
        CancellationToken cancellationToken = default)
    {
        // Cong kiem tra. Moi duong cham du lieu co pham vi gia dinh bat dau bang dong nay.
        var membership = await _familyContext.ResolveAsync(familyUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("NotAFamilyMember", "Ban khong thuoc gia dinh nay.");

        if (!membership.CanInvite)
            return OperationResult.Fail("Forbidden", "Chi chu ho hoac nguoi lon moi tao duoc ma moi.");

        var invite = new FamilyInvite
        {
            FamilyID = membership.FamilyID,
            Code = InviteCodeGenerator.Next(),
            CreatedByUserID = membership.UserID,
            ExpiresAt = DateTime.UtcNow.AddHours(request.ExpiresInHours),
            MaxUses = request.MaxUses
        };

        _db.FamilyInvites.Add(invite);
        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Ok("Da tao ma moi.", FamilyInviteResponse.From(invite));
    }

    public async Task<OperationResult> JoinAsync(
        Guid callerUserUuid,
        JoinFamilyRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        var userId = await _db.Users
            .Where(u => u.UserUUID == callerUserUuid)
            .Select(u => u.UserID)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId == Guid.Empty)
            return OperationResult.Fail("UserNotFound", "Khong tim thay tai khoan.");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var invite = await _db.FamilyInvites
            .Include(i => i.Family)
            .FirstOrDefaultAsync(i => i.Code == code, cancellationToken);

        // Ma het han, dung het luot, bi thu hoi va ma khong ton tai deu tra ve cung mot cau
        // tra loi. Phan biet chung nghia la noi cho nguoi do rang ma nay TUNG dung duoc —
        // du de biet minh doan gan trung va nen doan tiep.
        if (invite is null
            || invite.Family is null
            || !invite.Family.IsActive
            || !invite.IsUsable(DateTime.UtcNow))
        {
            return OperationResult.Fail("NotFound", "Ma moi khong hop le hoac da het han.");
        }

        var existing = await _db.FamilyMembers
            .FirstOrDefaultAsync(m => m.FamilyID == invite.FamilyID && m.UserID == userId, cancellationToken);

        if (existing is not null)
        {
            // Da o trong nha roi. Bam nham lan hai la chuyen binh thuong, va no khong duoc
            // an mot luot cua ma moi.
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                existing.JoinedDate = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                await _familyContext.InvalidateAsync(invite.Family.FamilyUUID, callerUserUuid, cancellationToken);
            }
            else
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return OperationResult.Ok("Ban da o trong gia dinh nay.");
        }

        _db.FamilyMembers.Add(new FamilyMember
        {
            FamilyID = invite.FamilyID,
            UserID = userId,
            MemberRole = FamilyMemberRole.Adult
        });

        // Dem luot va tao dong thanh vien phai cung thanh cong hoac cung that bai — day la
        // ly do ca hai nam trong mot transaction.
        invite.UsedCount += 1;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OperationResult.Ok("Da vao gia dinh.");
    }
```

New joiners get `Adult`, not `Child`: a code handed out by an adult is most often handed to another adult, and an Owner can demote afterwards (Task 8). The reverse default would silently deny the common case.

- [ ] **Step 6: Add the controller actions**

Add to `AioKin/Controllers/Family/FamiliesController.cs`:

```csharp
    /// <summary>Tao ma moi vao gia dinh. Owner va Adult goi duoc.</summary>
    [HttpPost("{uuid:guid}/invites")]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateInvite(
        Guid uuid,
        [FromBody] CreateInviteRequest request,
        CancellationToken cancellationToken)
        => this.ToActionResult(await _familyService.CreateInviteAsync(uuid, request, cancellationToken));

    /// <summary>Vao mot gia dinh bang ma moi.</summary>
    [HttpPost("join")]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Join(
        [FromBody] JoinFamilyRequest request,
        CancellationToken cancellationToken)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return this.ToActionResult(
                OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        return this.ToActionResult(await _familyService.JoinAsync(userUuid.Value, request, cancellationToken));
    }
```

`CreateInvite` does not read the token itself — `IFamilyContext` does that internally, which is the whole point of routing every scoped call through it.

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet test AioKin.sln --filter FullyQualifiedName~FamilyInviteTests
```

Expected: PASS, all five.

- [ ] **Step 8: Commit**

```bash
git add AioKin/Models AioKin/Services/Family AioKin/Controllers/Family AioKin.Tests/Family/FamilyInviteTests.cs
git commit -m "feat(family): tao ma moi va vao nhom bang ma

Ma het han, dung het luot, bi thu hoi va khong ton tai deu tra cung mot
cau tra loi: phan biet chung la noi cho nguoi do biet ma nay TUNG dung
duoc, tuc la ho doan gan trung.

Dem luot va tao dong thanh vien nam chung mot transaction."
```

---

### Task 8: Members — list, change role, remove and leave

The last task, and the one carrying the invariants. Cache invalidation lives here because this is where roles change.

**Files:**
- Create: `AioKin/Models/InputModel/Family/UpdateMemberRequest.cs`
- Create: `AioKin/Models/ViewModel/Family/FamilyMemberResponse.cs`
- Modify: `AioKin/Services/Family/IFamilyService.cs`
- Modify: `AioKin/Services/Family/FamilyService.cs`
- Modify: `AioKin/Controllers/Family/FamiliesController.cs`
- Modify: `README.md`
- Create: `AioKin.Tests/Family/FamilyMemberTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 5-7.
- Produces:
  - `IFamilyService.GetMembersAsync(Guid familyUuid, CancellationToken ct)` -> `Task<OperationResult>`
  - `IFamilyService.UpdateMemberAsync(Guid familyUuid, Guid memberUserUuid, UpdateMemberRequest request, CancellationToken ct)` -> `Task<OperationResult>`
  - `IFamilyService.RemoveMemberAsync(Guid familyUuid, Guid memberUserUuid, CancellationToken ct)` -> `Task<OperationResult>`
  - `FamilyMemberResponse` with `UserUuid`, `DisplayName`, `MemberRole`, `JoinedAtMillis`

Route note: the spec writes `/families/{uuid}/members/{memberId}`. This plan uses the member's **`UserUUID`**, not `FamilyMemberID`. The client already holds user UUIDs from the member list; exposing a second opaque id buys nothing and creates a second thing to look up.

- [ ] **Step 1: Write the failing test**

Create `AioKin.Tests/Family/FamilyMemberTests.cs`:

```csharp
using AioKin.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AioKin.Tests.Family;

[Collection(ApiCollection.Name)]
public class FamilyMemberTests
{
    private readonly ApiFixture _fixture;

    public FamilyMemberTests(ApiFixture fixture) => _fixture = fixture;

    private static async Task<Guid> CreateFamilyAsync(TestUser user, string name)
    {
        var response = await user.Client.PostAsJsonAsync("/families", new { name });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("familyUuid").GetGuid();
    }

    private static async Task<TestUser> AddMemberAsync(ApiFixture fixture, TestUser owner, Guid familyUuid)
    {
        var inviteResponse = await owner.Client.PostAsJsonAsync(
            $"/families/{familyUuid}/invites", new { maxUses = 5, expiresInHours = 24 });
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var code = invite.GetProperty("data").GetProperty("code").GetString()!;

        var member = await TestUser.CreateAsync(fixture);
        await member.Client.PostAsJsonAsync("/families/join", new { code });
        return member;
    }

    [Fact]
    public async Task Thanh_vien_xem_duoc_danh_sach_ca_nha()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha ds");
        await AddMemberAsync(_fixture, owner, familyUuid);

        var response = await owner.Client.GetFromJsonAsync<JsonElement>($"/families/{familyUuid}/members");

        Assert.Equal(2, response.GetProperty("data").EnumerateArray().Count());
    }

    [Fact]
    public async Task Nguoi_ngoai_khong_xem_duoc_danh_sach()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var outsider = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha kin ds");

        var response = await outsider.Client.GetAsync($"/families/{familyUuid}/members");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Owner_doi_duoc_vai_tro_thanh_vien()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha doi vai tro");
        var member = await AddMemberAsync(_fixture, owner, familyUuid);

        var response = await owner.Client.PatchAsJsonAsync(
            $"/families/{familyUuid}/members/{member.UserUuid}", new { memberRole = "Child" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mine = await member.Client.GetFromJsonAsync<JsonElement>("/families/me");
        Assert.Equal("Child", mine.GetProperty("data").EnumerateArray().First().GetProperty("myRole").GetString());
    }

    [Fact]
    public async Task Thanh_vien_thuong_khong_doi_duoc_vai_tro_nguoi_khac()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha khong loan");
        var first = await AddMemberAsync(_fixture, owner, familyUuid);
        var second = await AddMemberAsync(_fixture, owner, familyUuid);

        var response = await first.Client.PatchAsJsonAsync(
            $"/families/{familyUuid}/members/{second.UserUuid}", new { memberRole = "Child" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Owner_khong_tu_ha_cap_chinh_minh_khi_la_Owner_cuoi_cung()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha mot chu");

        var response = await owner.Client.PatchAsJsonAsync(
            $"/families/{familyUuid}/members/{owner.UserUuid}", new { memberRole = "Adult" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Owner_go_duoc_thanh_vien_va_quyen_mat_ngay()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha go");
        var member = await AddMemberAsync(_fixture, owner, familyUuid);

        // Nap cache cua thanh vien do truoc khi go — day chinh la truong hop can chan.
        await member.Client.GetAsync($"/families/{familyUuid}/members");

        var removal = await owner.Client.DeleteAsync($"/families/{familyUuid}/members/{member.UserUuid}");
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);

        var afterRemoval = await member.Client.GetAsync($"/families/{familyUuid}/members");
        Assert.Equal(HttpStatusCode.Forbidden, afterRemoval.StatusCode);
    }

    [Fact]
    public async Task Thanh_vien_tu_roi_nhom_duoc()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha roi di");
        var member = await AddMemberAsync(_fixture, owner, familyUuid);

        var response = await member.Client.DeleteAsync($"/families/{familyUuid}/members/{member.UserUuid}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mine = await member.Client.GetFromJsonAsync<JsonElement>("/families/me");
        Assert.Empty(mine.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Owner_cuoi_cung_khong_roi_nhom_duoc()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha chu cuoi");

        var response = await owner.Client.DeleteAsync($"/families/{familyUuid}/members/{owner.UserUuid}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
```

`PatchAsJsonAsync` needs `using System.Net.Http.Json;` — it is available in .NET 9.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test AioKin.sln --filter FullyQualifiedName~FamilyMemberTests
```

Expected: FAIL — 404 on the member routes.

- [ ] **Step 3: Write the models**

Create `AioKin/Models/InputModel/Family/UpdateMemberRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using AioKin.Data.Entities.Family;

namespace AioKin.Models.InputModel.Family;

public class UpdateMemberRequest
{
    /// <summary>Owner, Adult hoac Child. Nhan chuoi chu khong nhan so — doc log de hieu hon.</summary>
    [Required]
    public required string MemberRole { get; set; }

    /// <summary>Ten hien thi trong nha. Bo trong thi giu nguyen ten dang co.</summary>
    [MaxLength(120)]
    public string? DisplayName { get; set; }

    public bool TryParseRole(out FamilyMemberRole role)
        => Enum.TryParse(MemberRole, ignoreCase: true, out role) && Enum.IsDefined(role);
}
```

`Enum.IsDefined` matters: `Enum.TryParse` happily returns `true` for `"7"` and yields an undefined enum value, which would then be written to the database as the string `"7"`.

Create `AioKin/Models/ViewModel/Family/FamilyMemberResponse.cs`:

```csharp
namespace AioKin.Models.ViewModel.Family;

public class FamilyMemberResponse
{
    public Guid UserUuid { get; set; }

    public string? DisplayName { get; set; }

    public string MemberRole { get; set; } = string.Empty;

    public long JoinedAtMillis { get; set; }
}
```

- [ ] **Step 4: Extend the service interface**

Add to `AioKin/Services/Family/IFamilyService.cs`:

```csharp
    /// <summary>Danh sach thanh vien. Moi thanh vien deu xem duoc.</summary>
    Task<OperationResult> GetMembersAsync(
        Guid familyUuid,
        CancellationToken cancellationToken = default);

    /// <summary>Doi vai tro hoac ten hien thi cua mot thanh vien. Chi Owner.</summary>
    Task<OperationResult> UpdateMemberAsync(
        Guid familyUuid,
        Guid memberUserUuid,
        UpdateMemberRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Go mot thanh vien (Owner), hoac tu roi nhom (chinh minh).</summary>
    Task<OperationResult> RemoveMemberAsync(
        Guid familyUuid,
        Guid memberUserUuid,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Implement the three methods**

Add to `AioKin/Services/Family/FamilyService.cs`:

```csharp
    public async Task<OperationResult> GetMembersAsync(
        Guid familyUuid,
        CancellationToken cancellationToken = default)
    {
        var membership = await _familyContext.ResolveAsync(familyUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("NotAFamilyMember", "Ban khong thuoc gia dinh nay.");

        var members = await _db.FamilyMembers
            .AsNoTracking()
            .Where(m => m.FamilyID == membership.FamilyID && m.IsActive)
            // Sap theo ngay vao nha, KHONG theo vai tro: MemberRole luu duoi dang chuoi
            // (HasConversion<string>), nen ORDER BY tren no ra Adult, Child, Owner theo bang
            // chu cai — dung nguoc voi y dinh. Chu ho la nguoi tao nha nen vao truoc tien,
            // va sap theo JoinedDate thi tu no dung dau danh sach.
            .OrderBy(m => m.JoinedDate)
            .ThenBy(m => m.FamilyMemberID)
            .Select(m => new FamilyMemberResponse
            {
                UserUuid = m.User!.UserUUID,
                DisplayName = m.DisplayName,
                MemberRole = m.MemberRole.ToString(),
                JoinedAtMillis = new DateTimeOffset(
                    DateTime.SpecifyKind(m.JoinedDate, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
            })
            .ToListAsync(cancellationToken);

        return OperationResult.Ok(data: members);
    }

    public async Task<OperationResult> UpdateMemberAsync(
        Guid familyUuid,
        Guid memberUserUuid,
        UpdateMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var membership = await _familyContext.ResolveAsync(familyUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("NotAFamilyMember", "Ban khong thuoc gia dinh nay.");

        if (!membership.IsOwner)
            return OperationResult.Fail("Forbidden", "Chi chu ho moi doi duoc vai tro thanh vien.");

        if (!request.TryParseRole(out var newRole))
            return OperationResult.Fail("ValidationError", "Vai tro phai la Owner, Adult hoac Child.");

        var target = await _db.FamilyMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(
                m => m.FamilyID == membership.FamilyID && m.User!.UserUUID == memberUserUuid && m.IsActive,
                cancellationToken);

        if (target is null)
            return OperationResult.Fail("NotFound", "Khong tim thay thanh vien nay.");

        // Ha cap Owner cuoi cung nghia la gia dinh khong con ai doi duoc vai tro, khong con ai
        // go duoc thanh vien, va khong con ai lay lai quyen do — mot the ket khong loi ra.
        if (target.MemberRole == FamilyMemberRole.Owner && newRole != FamilyMemberRole.Owner)
        {
            var otherOwners = await _db.FamilyMembers.CountAsync(
                m => m.FamilyID == membership.FamilyID
                  && m.IsActive
                  && m.MemberRole == FamilyMemberRole.Owner
                  && m.FamilyMemberID != target.FamilyMemberID,
                cancellationToken);

            if (otherOwners == 0)
                return OperationResult.Fail("Conflict", "Gia dinh phai luon con it nhat mot chu ho.");
        }

        target.MemberRole = newRole;
        if (request.DisplayName is not null)
            target.DisplayName = request.DisplayName.Trim();

        // ExecuteUpdateAsync ghi thang xuong database ngay lap tuc, con SaveChangesAsync thi
        // ghi sau. Khong boc chung trong mot transaction thi mot loi o giua se de lai
        // OwnerUserID da doi trong khi vai tro thi chua — gia dinh co mot chu ho tren giay to
        // ma khong co quyen gi.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (newRole == FamilyMemberRole.Owner)
            await _db.Families
                .Where(f => f.FamilyID == membership.FamilyID)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.OwnerUserID, target.UserID), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Xoa cache NGAY. Con lai thi nguoi vua bi ha cap van hanh dong voi quyen cu them 5 phut.
        await _familyContext.InvalidateAsync(familyUuid, memberUserUuid, cancellationToken);

        return OperationResult.Ok("Da cap nhat thanh vien.");
    }

    public async Task<OperationResult> RemoveMemberAsync(
        Guid familyUuid,
        Guid memberUserUuid,
        CancellationToken cancellationToken = default)
    {
        var membership = await _familyContext.ResolveAsync(familyUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("NotAFamilyMember", "Ban khong thuoc gia dinh nay.");

        var isSelf = membership.UserUUID == memberUserUuid;

        // Owner go duoc bat ky ai; ai cung tu roi nhom duoc. Ngoai hai truong hop do thi khong.
        if (!membership.IsOwner && !isSelf)
            return OperationResult.Fail("Forbidden", "Chi chu ho moi go duoc thanh vien khac.");

        var target = await _db.FamilyMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(
                m => m.FamilyID == membership.FamilyID && m.User!.UserUUID == memberUserUuid && m.IsActive,
                cancellationToken);

        if (target is null)
            return OperationResult.Fail("NotFound", "Khong tim thay thanh vien nay.");

        if (target.MemberRole == FamilyMemberRole.Owner)
        {
            var otherOwners = await _db.FamilyMembers.CountAsync(
                m => m.FamilyID == membership.FamilyID
                  && m.IsActive
                  && m.MemberRole == FamilyMemberRole.Owner
                  && m.FamilyMemberID != target.FamilyMemberID,
                cancellationToken);

            if (otherOwners == 0)
                return OperationResult.Fail(
                    "Conflict",
                    "Chu ho cuoi cung khong roi nhom duoc. Hay chuyen quyen chu ho cho nguoi khac truoc.");
        }

        // Soft delete: lich su chi tieu va tin nhan cua nguoi da roi di van phai doc duoc, va
        // vao lai nhom sau nay thi bat lai dong cu chu khong tao dong moi.
        target.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);

        await _familyContext.InvalidateAsync(familyUuid, memberUserUuid, cancellationToken);

        return OperationResult.Ok(isSelf ? "Da roi gia dinh." : "Da go thanh vien.");
    }
```

Add `using AioKin.Models.ViewModel.Family;` if not already present.

- [ ] **Step 6: Add the controller actions**

Add to `AioKin/Controllers/Family/FamiliesController.cs`:

```csharp
    /// <summary>Danh sach thanh vien cua gia dinh.</summary>
    [HttpGet("{uuid:guid}/members")]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMembers(Guid uuid, CancellationToken cancellationToken)
        => this.ToActionResult(await _familyService.GetMembersAsync(uuid, cancellationToken));

    /// <summary>Doi vai tro hoac ten hien thi cua mot thanh vien. Chi chu ho.</summary>
    [HttpPatch("{uuid:guid}/members/{memberUserUuid:guid}")]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMember(
        Guid uuid,
        Guid memberUserUuid,
        [FromBody] UpdateMemberRequest request,
        CancellationToken cancellationToken)
        => this.ToActionResult(
            await _familyService.UpdateMemberAsync(uuid, memberUserUuid, request, cancellationToken));

    /// <summary>Go mot thanh vien, hoac tu roi nhom khi <c>memberUserUuid</c> la chinh minh.</summary>
    [HttpDelete("{uuid:guid}/members/{memberUserUuid:guid}")]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveMember(
        Guid uuid,
        Guid memberUserUuid,
        CancellationToken cancellationToken)
        => this.ToActionResult(
            await _familyService.RemoveMemberAsync(uuid, memberUserUuid, cancellationToken));
```

- [ ] **Step 7: Run the whole suite**

```bash
dotnet test AioKin.sln
```

Expected: PASS, every test from Tasks 1-8.

- [ ] **Step 8: Document the endpoints**

Add to `README.md` after the `/posts` · `/todos` · `/ability` section:

```markdown
### `/families` — ho gia dinh

Pham vi chia se ma chat nhom, kho file va so chi tieu se dua tren. Danh tinh lay tu token;
`uuid` tren duong dan la gia dinh dang thao tac va luon di qua `IFamilyContext` truoc khi
cham du lieu.

| Method | Duong dan | Quyen |
|---|---|---|
| POST | `/families` | `Customer` — nguoi tao thanh Owner |
| GET | `/families/me` | Cac gia dinh minh thuoc |
| POST | `/families/{uuid}/invites` | Owner / Adult |
| POST | `/families/join` | `Customer` — body `{ code }` |
| GET | `/families/{uuid}/members` | Thanh vien |
| PATCH | `/families/{uuid}/members/{userUuid}` | Owner |
| DELETE | `/families/{uuid}/members/{userUuid}` | Owner, hoac chinh minh de roi nhom |

`MemberRole` (`Owner` / `Adult` / `Child`) la vai tro trong pham vi mot gia dinh, khong phai
role he thong — role trong token van la `Customer`.

Gia dinh luon con it nhat mot Owner: ha cap hoac go Owner cuoi cung tra 409. Go thanh vien
la soft delete, va cache tu cach thanh vien trong Redis bi xoa ngay — nguoi vua bi go mat
quyen tuc thi chu khong phai sau 5 phut.
```

- [ ] **Step 9: Run `detect_changes` and commit**

Per `CLAUDE.md`, before committing:

```
detect_changes({repo: "AioKin", scope: "all"})
```

Confirm nothing outside the family scope, `Program.cs`, `DbSeeder`, `RedisKeys` and `OperationResultHttpExtensions` is affected. In particular no execution flow touching `/posts`, `/todos` or `/ability` should appear.

```bash
git add AioKin AioKin.Tests README.md
git commit -m "feat(family): quan ly thanh vien, doi vai tro va roi nhom

Gia dinh luon con it nhat mot Owner: ha cap hay go Owner cuoi cung deu
tra 409, neu khong gia dinh roi vao trang thai khong ai lay lai duoc
quyen quan tri.

Go thanh vien la soft delete de lich su chi tieu va tin nhan cua nguoi
da roi di van doc duoc. Cache tu cach thanh vien xoa ngay tai cho doi
vai tro va cho go — cham mot nhip la quyen cu con song them 5 phut."
```

- [ ] **Step 10: Open the pull request**

```bash
git push -u origin feat/family-core
gh pr create --base main --title "M0: nen mong ho gia dinh" --body "Trien khai muc 2 cua docs/ke-hoach-mo-rong.md. Xem docs/superpowers/plans/2026-09-10-m0-family-core.md."
```

---

## Self-Review

**Spec coverage (section 2 of `docs/ke-hoach-mo-rong.md`):**

| Spec item | Task |
|---|---|
| `Family` entity | 2 |
| `FamilyMember` entity, UNIQUE(FamilyID, UserID), INDEX(UserID) | 2 |
| `FamilyInvite` entity | 2 |
| `Family.InviteCode` | **Deliberately dropped** — see "Deviation From The Spec" |
| `MemberRole` is in-family, not a system role | 2 (enum doc comment), 8 (README) |
| `POST /families` | 6 |
| `GET /families/me` | 6 |
| `POST /families/{uuid}/invites` (Owner/Adult) | 7 |
| `POST /families/join` | 7 |
| `GET /families/{uuid}/members` | 8 |
| `PATCH /families/{uuid}/members/{id}` (Owner) | 8 |
| `DELETE /families/{uuid}/members/{id}` (Owner or self) | 8 |
| `IFamilyContext.ResolveAsync` returning `FamilyMembership?` | 5 |
| Redis cache `family:{uuid}:member:{userUuid}`, 5-min TTL | 5 |
| Cache deleted immediately on role change / removal | 5 (API), 8 (call sites + test) |
| `NotAFamilyMember` -> 403 (section 1.2) | 3 |
| `SubjectType` on every new entity + seeded rules (section 1.1) | 2, 3 |
| Migration named `AddFamily` (section 9.6) | 2 |
| Invite codes expire and have a use cap (section 9.4.8) | 2 (entity), 7 (enforcement + test) |

Section 1.2's other five error codes belong to M3 and M6 and are intentionally absent.

**Placeholder scan:** no TBD/TODO; every code step carries real code; no step says "similar to Task N". Three places defer to the codebase rather than assert — `User`'s required properties (Task 2 Step 1), `IJwtTokenService`'s signature (Task 6 Step 1), and the generated migration's contents (Task 2 Step 7). Each is marked with a blockquote and names the file to read. These are deliberate: this plan must not invent signatures for code it did not read line by line.

**Type consistency:** `FamilyMembership` is constructed with five positional values in `FamilyContext.CachedMembership.ToMembership()` and consumed as `.FamilyID`, `.UserID`, `.UserUUID`, `.Role`, `.CanInvite`, `.IsOwner` in Tasks 6-8 — all six members exist on the record. `IFamilyService` grows across Tasks 6, 7 and 8; every method added to the interface is implemented in the same task. `FamilyResponse.From(family, myRole, memberCount)` is called with exactly three arguments in both call sites. `RedisKeys.FamilyMembership` / `FamilyMembershipPrefix` and `RedisTtl.FamilyMembership` are defined in Task 5 Step 5 and used only in Task 5 Step 6. `InviteCodeGenerator.Next()` and `.Alphabet` match between Task 4 and Task 7.

**Scope:** one milestone, one branch, one migration, eight tasks. M1-M7 get their own plans.

---

## What This Plan Does Not Cover

M0 only. The remaining milestones each need their own plan document, written against the same spec:

| Milestone | Plan file | Blocked by |
|---|---|---|
| M1 Goong places | `2026-XX-XX-m1-goong-places.md` | M0 |
| M2 Realtime chat | `2026-XX-XX-m2-realtime-chat.md` | M0 |
| M3 Media storage | `2026-XX-XX-m3-media-storage.md` | M0 |
| M4 Expenses | `2026-XX-XX-m4-expenses.md` | M0, M3 |
| M5 Schedule v2 | `2026-XX-XX-m5-schedule-v2.md` | M0, M1 |
| M6 QR + transfer intents | `2026-XX-XX-m6-qr-payment.md` | M0, M3, M4, M5 |
| M7 Hardening | `2026-XX-XX-m7-hardening.md` | all |

Each is written just before its milestone begins, not now. A plan for M2 written today would have to name the exact signature of `IFamilyContext.ResolveAsync` as it actually ends up — and inventing those names in advance produces precisely the "references to types not defined in any task" failure that makes a plan unusable.
