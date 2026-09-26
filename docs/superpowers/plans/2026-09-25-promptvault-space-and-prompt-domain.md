# PromptVault — Space Model & Prompt Domain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `Space` (personal/family/team) as a thin layer over the existing Family core, and add the Prompt domain (categories, tags, prompts, variables) scoped to a Space, with read-only browsing endpoints. Mutations (create/update/delete) for the Prompt domain are deliberately **not** built here — see Architecture.

**Architecture:** `Space` generalizes "who can see this content" without touching Family's shipped tables: a `family`-type Space just points at an existing `family.families` row via `FamilyID`, and membership questions for it are answered by delegating to the existing `IFamilyContext` — never duplicated into a `space_members` row. `team`-type Spaces are the one genuinely new membership model, with a trimmed Owner/Admin/Member role set. `personal`-type Spaces are auto-created, one per user, no membership row at all (the owner check IS the membership check). A new `ISpaceContext` (same shape as `IFamilyContext`) answers "is this caller a member, with what capability" for any Space regardless of type, and every Prompt-domain query goes through it — mirroring how `IFamilyContext` already gates M0.

Prompt/Category/Tag/PromptVariable get **no POST/PUT/DELETE controller actions in this plan.** Per the sync design (`docs/superpowers/specs/2026-09-25-promptvault-merge-design.md` section 6), every mutation to these four entity types is meant to go through the offline-first outbox → `/sync/push` path on both clients, even when the device is online — that endpoint is built in the follow-up plan, `docs/superpowers/plans/2026-09-25-promptvault-sync-engine.md`. Building a second, parallel "just POST it" mutation path here would let a caller bypass version tracking and conflict detection entirely. This plan only builds the entities, the migration, Space management (which is *not* sync-tracked — created online, like Family), and read-side browsing.

**Tech Stack:** .NET 9, EF Core 9 + Npgsql, EFCore.NamingConventions (snake_case), StackExchange.Redis behind `IRedisService`, xUnit + `Microsoft.AspNetCore.Mvc.Testing` against real Postgres (existing `AioKin.Tests` harness).

**Spec:** `docs/superpowers/specs/2026-09-25-promptvault-merge-design.md` — sections 3 (SQL corrections), 4 (Space model), 5 (Prompt domain scope), 8 (EF migration vs. draft SQL).

## Global Constraints

- **Comments and docs are Vietnamese without diacritics** ("khong dau"), matching the rest of the repo.
- **Identity always comes from the token.** No endpoint accepts a caller-identity parameter.
- **Family's existing tables are never modified or renamed.** `family.families`, `family.family_members`, `family.family_invites` stay exactly as M0 shipped them. A `family`-type Space only adds a pointer to an existing family row.
- **`Prompt`, `Category`, `Tag`, `PromptVariable` primary keys are client-generated UUIDs, not server-generated.** This is a deliberate deviation from the `EntityID`/`EntityUUID` split every other top-level entity in this repo uses (`User.UserID`/`UserUUID`, `Family.FamilyID`/`FamilyUUID`) — those four are the only entities the offline-first sync design (next plan) ever creates client-side before the server has seen them; splitting an internal/public id for them would force local-id-to-server-id remapping after every sync, which is exactly what the client-generated-id design in the spec exists to avoid. `Space` is **not** part of that offline-sync set (see the spec's `sync_log.entity_type` check constraint: `'prompt' | 'category' | 'tag' | 'prompt_variable'`, no `'space'`) and keeps the normal `SpaceID`/`SpaceUUID` split.
- **Every new entity exposed through a generic ability-checked surface MUST declare `SubjectType` and be added to `DbSeeder.SeedRolePermissionsAsync`** (repo convention, `ke-hoach-mo-rong.md` section 1.1). `Space`, `Prompt`, `Category`, `Tag` all get one. `SpaceMember` does not need its own rule for this plan (no endpoint returns raw `SpaceMember` rows to the client outside what `SpacesController` already shapes).
- **CASL rule order is semantic** — a later rule overrides an earlier one; no step in this plan reorders `DbSeeder`'s existing `customerRules` array, only appends.
- **Table and column names are snake_case**, produced by the existing `UseSnakeCaseNamingConvention()`.
- **Run `impact({target: "FamilyService", direction: "upstream", repo: "AioKin"})` before Task 3** — that task adds two lines to `FamilyService.CreateAsync`, shipped M0 code.
- **Run `detect_changes()` before the final commit of this plan.**

## Review Focus

- **A user with no families and no teams** must still get a working Prompt experience through their auto-created personal Space — the "first use" path must not 404 or require an explicit "create my personal space" call from the client.
- **A Family created before this plan existed** has no matching Space row yet — the backfill step must cover it, or that family's members get a broken/missing Prompt experience with no error explaining why.
- **A `Child`-role family member** must still be able to read and create prompts in that family's Space (this plan intentionally does not restrict create by family role — see Task 2) but must not be able to delete another member's prompt.
- **Removing a team member** (or a family member losing access via M0's own flow) must make their next `ISpaceContext.ResolveAsync` call return `null` promptly — caching must invalidate the same way `IFamilyContext`'s does, not silently hold a stale grant for the cache TTL.
- **A space of type `team` must never resolve membership through `family_members`, and a space of type `family` must never resolve through `space_members`** — a bug that fell through to the wrong table would look like it works (both tables can return *a* row) while enforcing the wrong role set.

---

## Prerequisites

```bash
docker run -d --name aiokin-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16-alpine
docker run -d --name aiokin-rd -p 6379:6379 redis:7-alpine
```

```bash
git checkout feat/family-core
git pull
git checkout -b feat/promptvault-space-and-prompt-domain
```

---

## File Structure

**New — production:**

| File | Responsibility |
|---|---|
| `AioKin/Data/Entities/Vault/SpaceType.cs` | `Personal \| Family \| Team` enum |
| `AioKin/Data/Entities/Vault/Space.cs` | The Space row; `FamilyID` set only when `SpaceType == Family` |
| `AioKin/Data/Entities/Vault/SpaceMemberRole.cs` | `Owner \| Admin \| Member` enum (Team only) |
| `AioKin/Data/Entities/Vault/SpaceMember.cs` | Team membership join row |
| `AioKin/Data/Entities/Vault/Category.cs` | Client-generated id, per-space |
| `AioKin/Data/Entities/Vault/Tag.cs` | Client-generated id, per-space |
| `AioKin/Data/Entities/Vault/PromptTag.cs` | Many-to-many join, composite key, not independently sync-tracked |
| `AioKin/Data/Entities/Vault/Prompt.cs` | Client-generated id; tiered-storage + sync columns from the spec |
| `AioKin/Data/Entities/Vault/PromptVariable.cs` | Client-generated id, per-prompt |
| `AioKin/Services/Vault/ISpaceContext.cs` | The one membership question, any space type |
| `AioKin/Services/Vault/SpaceContext.cs` | Implementation — delegates to `IFamilyContext` for family-type spaces |
| `AioKin/Services/Vault/SpaceMembership.cs` | Record returned by `ResolveAsync` |
| `AioKin/Services/Vault/ISpaceService.cs` | Personal auto-create, team create/add-member, list mine |
| `AioKin/Services/Vault/SpaceService.cs` | Implementation |
| `AioKin/Services/Vault/IPromptBrowseService.cs` | Read-only: list/get prompts, categories, tags in a space |
| `AioKin/Services/Vault/PromptBrowseService.cs` | Implementation |
| `AioKin/Controllers/Vault/SpacesController.cs` | Create team, add/list members, list my spaces |
| `AioKin/Controllers/Vault/PromptsController.cs` | `GET` only — list/get prompts (with tags + variables), categories, tags |
| `AioKin/Models/InputModel/Vault/CreateTeamSpaceRequest.cs`, `AddSpaceMemberRequest.cs` | |
| `AioKin/Models/ViewModel/Vault/SpaceResponse.cs`, `PromptSummaryResponse.cs`, `PromptDetailResponse.cs`, `CategoryResponse.cs`, `TagResponse.cs` | |
| `AioKin.Tests/Vault/SpaceContextTests.cs`, `SpaceEndpointTests.cs`, `PromptBrowseEndpointTests.cs`, `PromptVaultSchemaTests.cs` | |

**Modified:**

| File | Change |
|---|---|
| `AioKin/Data/AioKinDbContext.cs` | New `DbSet`s + `OnModelCreating` entries |
| `AioKin/Data/DbSeeder.cs` | `SubjectType` rules for `Space`, `Prompt`, `Category`, `Tag`; personal-space backfill for existing users |
| `AioKin/Services/Family/FamilyService.cs` | `CreateAsync` also creates the linked `family`-type Space, same transaction |
| `AioKin/Program.cs` | DI registration for the 3 new services |

---

### Task 1: `Space` model — entities, DbContext, migration

**Files:**
- Create: `AioKin/Data/Entities/Vault/SpaceType.cs`, `Space.cs`, `SpaceMemberRole.cs`, `SpaceMember.cs`
- Modify: `AioKin/Data/AioKinDbContext.cs`
- Create (via CLI): migration `AddPromptVaultSpaces`

**Interfaces:**
- Produces: `Space` (`SpaceID`, `SpaceUUID`, `SpaceType`, `Name`, `OwnerUserID`, `FamilyID?`, `IsActive`, `CreatedDate`), `SpaceMember` (`SpaceMemberID`, `SpaceID`, `UserID`, `MemberRole`, `JoinedDate`).

- [ ] **Step 1: `SpaceType.cs`**

```csharp
namespace AioKin.Data.Entities.Vault;

/// <summary>
/// Loai pham vi chia se. Family khong tao lai co che thanh vien — no tro toi
/// family.families qua FamilyID va uy quyen cau hoi thanh vien cho IFamilyContext.
/// </summary>
public enum SpaceType { Personal = 0, Family = 1, Team = 2 }
```

- [ ] **Step 2: `Space.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Data.Entities.Vault;

/// <summary>
/// Pham vi chia se cho Prompt/Category/Tag. KHONG phai nguon su that ve thanh vien cho
/// SpaceType.Family — nguon that van la family.family_members, xem SpaceContext.
/// </summary>
[Table("spaces", Schema = "vault")]
public class Space
{
    public const string SubjectType = "Space";

    [Key]
    public Guid SpaceID { get; set; } = Guid.NewGuid();

    /// <summary>Id cong khai — moi route/DTO dung no, khong lo SpaceID noi bo.</summary>
    public Guid SpaceUUID { get; set; } = Guid.NewGuid();

    public SpaceType SpaceType { get; set; }

    [MaxLength(120)]
    public required string Name { get; set; }

    public Guid OwnerUserID { get; set; }

    /// <summary>Chi co gia tri khi SpaceType = Family. 1 family = 1 space (unique).</summary>
    public Guid? FamilyID { get; set; }

    [ForeignKey(nameof(FamilyID))]
    public FamilyDb? Family { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 3: `SpaceMemberRole.cs` + `SpaceMember.cs`**

```csharp
namespace AioKin.Data.Entities.Vault;

/// <summary>Vai tro trong 1 Team space. Khong dung chung voi FamilyMemberRole — ngu canh khac nhau.</summary>
public enum SpaceMemberRole { Owner = 0, Admin = 1, Member = 2 }
```

```csharp
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Data.Entities.Vault;

/// <summary>Thanh vien cua 1 Team space. KHONG dung cho Family/Personal — xem SpaceContext.</summary>
public class SpaceMember
{
    [Key]
    public Guid SpaceMemberID { get; set; } = Guid.NewGuid();

    public Guid SpaceID { get; set; }

    [ForeignKey(nameof(SpaceID))]
    public Space? Space { get; set; }

    public Guid UserID { get; set; }

    [ForeignKey(nameof(UserID))]
    public UserDb? User { get; set; }

    public SpaceMemberRole MemberRole { get; set; } = SpaceMemberRole.Member;

    public DateTime JoinedDate { get; set; } = DateTime.UtcNow;
}
```

(add the missing `using System.ComponentModel.DataAnnotations;` and `using System.ComponentModel.DataAnnotations.Schema;` to `SpaceMember.cs`.)

- [ ] **Step 4: Wire into `AioKinDbContext.cs`**

```csharp
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<SpaceMember> SpaceMembers => Set<SpaceMember>();
```

```csharp
        modelBuilder.Entity<Space>(entity =>
        {
            entity.HasIndex(s => s.SpaceUUID).IsUnique();

            // 1 family = 1 space. Partial-unique qua filter EF sinh tu Where-tuong-duong:
            // dung HasFilter truc tiep vi FamilyID la nullable va chi Family-type moi dat no.
            entity.HasIndex(s => s.FamilyID).IsUnique().HasFilter("family_id IS NOT NULL");

            entity.HasOne(s => s.Family)
                .WithMany()
                .HasForeignKey(s => s.FamilyID)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpaceMember>(entity =>
        {
            entity.HasIndex(m => new { m.SpaceID, m.UserID }).IsUnique();
            entity.HasIndex(m => m.UserID);

            entity.HasOne(m => m.Space)
                .WithMany()
                .HasForeignKey(m => m.SpaceID)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserID)
                .OnDelete(DeleteBehavior.Cascade);
        });
```

Add `using AioKin.Data.Entities.Vault;` to the top of `AioKinDbContext.cs`.

- [ ] **Step 5: Generate the migration**

```bash
cd AioKin
dotnet ef migrations add AddPromptVaultSpaces --output-dir Data/Migrations
cd ..
```

Open the generated migration and confirm: schema `vault` created if missing, `spaces` table with the unique index on `space_uuid` and the partial unique index on `family_id`, `space_members` table with the composite unique index, both FKs cascading as written above.

- [ ] **Step 6: Write the schema/unique-constraint test**

```csharp
using AioKin.Data.Entities.Vault;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Vault;

[Collection(ApiCollection.Name)]
public class PromptVaultSchemaTests
{
    private readonly ApiFixture _fixture;

    public PromptVaultSchemaTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Mot_family_khong_the_gan_hai_space()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        db.Users.Add(user);
        var family = new AioKin.Data.Entities.Family.Family { Name = "Nha A", OwnerUserID = user.UserID };
        db.Families.Add(family);
        await db.SaveChangesAsync();

        db.Spaces.Add(new Space { SpaceType = SpaceType.Family, Name = "Nha A", OwnerUserID = user.UserID, FamilyID = family.FamilyID });
        await db.SaveChangesAsync();

        db.Spaces.Add(new Space { SpaceType = SpaceType.Family, Name = "Nha A (2)", OwnerUserID = user.UserID, FamilyID = family.FamilyID });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
```

- [ ] **Step 7: Run and commit**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~PromptVaultSchemaTests` → PASS.

```bash
git add -A
git commit -m "feat(vault): add Space and SpaceMember entities"
```

---

### Task 2: `ISpaceContext` / `SpaceContext` / `SpaceMembership`

**Files:**
- Create: `AioKin/Services/Vault/SpaceMembership.cs`, `ISpaceContext.cs`, `SpaceContext.cs`
- Create: `AioKin.Tests/Vault/SpaceContextTests.cs`
- Modify: `AioKin/Program.cs` (DI registration)

**Interfaces:**
- Consumes: `IFamilyContext.ResolveAsync` (existing, unchanged), `AioKinDbContext.Spaces`/`SpaceMembers`, `IRedisService`.
- Produces: `SpaceMembership(SpaceID, SpaceUUID, SpaceType, UserID, UserUUID, CanManage)`, `ISpaceContext.ResolveAsync(Guid spaceUuid, CancellationToken)`.

- [ ] **Step 1: `SpaceMembership.cs`**

```csharp
using AioKin.Data.Entities.Vault;

namespace AioKin.Services.Vault;

/// <summary>
/// Ket qua kiem tra thanh vien, bat ke Space la Personal/Family/Team. CanManage la kha nang
/// duy nhat can cho pham vi plan nay: sua/xoa noi dung cua nguoi khac trong space. Tac gia tu
/// sua/xoa bai cua chinh minh luon duoc phep — kiem tra do nam o tang service goi ham nay,
/// khong nam trong CanManage.
/// </summary>
/// <param name="SpaceID">Id noi bo — dung cho khoa ngoai.</param>
/// <param name="SpaceUUID">Id cong khai.</param>
/// <param name="UserID">Id noi bo cua nguoi goi.</param>
/// <param name="UserUUID">Id cong khai cua nguoi goi, lay tu token.</param>
public sealed record SpaceMembership(
    Guid SpaceID,
    Guid SpaceUUID,
    SpaceType SpaceType,
    Guid UserID,
    Guid UserUUID,
    bool CanManage);
```

- [ ] **Step 2: `ISpaceContext.cs`**

```csharp
namespace AioKin.Services.Vault;

/// <summary>
/// Tra loi duy nhat mot cau hoi cho ca 3 loai Space: nguoi goi request nay co phai thanh
/// vien cua spaceUuid khong, va co quyen quan ly khong. Moi service cham du lieu Prompt
/// domain phai goi ResolveAsync truoc, giong het cach IFamilyContext gate M0.
/// </summary>
public interface ISpaceContext
{
    Task<SpaceMembership?> ResolveAsync(Guid spaceUuid, CancellationToken cancellationToken = default);

    Task InvalidateAsync(Guid spaceUuid, Guid userUuid, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write the failing tests**

```csharp
using AioKin.Data.Entities.Family;
using AioKin.Data.Entities.Vault;
using AioKin.Services.Vault;
using AioKin.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Tests.Vault;

[Collection(ApiCollection.Name)]
public class SpaceContextTests
{
    private readonly ApiFixture _fixture;

    public SpaceContextTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Personal_space_chi_owner_moi_resolve_duoc_va_luon_CanManage()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var owner = TestData.NewUser();
        var stranger = TestData.NewUser();
        db.Users.AddRange(owner, stranger);
        var space = new Space { SpaceType = SpaceType.Personal, Name = "Personal", OwnerUserID = owner.UserID };
        db.Spaces.Add(space);
        await db.SaveChangesAsync();

        var httpContext = TestHttpContext.ForUser(owner.UserUUID);
        var ctx = BuildContext(scope, httpContext);
        var membership = await ctx.ResolveAsync(space.SpaceUUID);

        Assert.NotNull(membership);
        Assert.True(membership!.CanManage);

        var strangerCtx = BuildContext(scope, TestHttpContext.ForUser(stranger.UserUUID));
        Assert.Null(await strangerCtx.ResolveAsync(space.SpaceUUID));
    }

    [Fact]
    public async Task Family_space_uy_quyen_dung_cho_IFamilyContext_khong_dung_space_members()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var owner = TestData.NewUser();
        db.Users.Add(owner);
        var family = new FamilyDb { Name = "Nha A", OwnerUserID = owner.UserID };
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember { FamilyID = family.FamilyID, UserID = owner.UserID, MemberRole = FamilyMemberRole.Owner });
        var space = new Space { SpaceType = SpaceType.Family, Name = "Nha A", OwnerUserID = owner.UserID, FamilyID = family.FamilyID };
        db.Spaces.Add(space);
        await db.SaveChangesAsync();

        var ctx = BuildContext(scope, TestHttpContext.ForUser(owner.UserUUID));
        var membership = await ctx.ResolveAsync(space.SpaceUUID);

        Assert.NotNull(membership);
        Assert.Equal(SpaceType.Family, membership!.SpaceType);
        Assert.True(membership.CanManage);
    }

    [Fact]
    public async Task Team_space_dung_space_members_khong_dung_family()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var owner = TestData.NewUser();
        var member = TestData.NewUser();
        db.Users.AddRange(owner, member);
        var space = new Space { SpaceType = SpaceType.Team, Name = "Team A", OwnerUserID = owner.UserID };
        db.Spaces.Add(space);
        db.SpaceMembers.Add(new SpaceMember { SpaceID = space.SpaceID, UserID = member.UserID, MemberRole = SpaceMemberRole.Member });
        await db.SaveChangesAsync();

        var memberCtx = BuildContext(scope, TestHttpContext.ForUser(member.UserUUID));
        var membership = await memberCtx.ResolveAsync(space.SpaceUUID);

        Assert.NotNull(membership);
        Assert.False(membership!.CanManage);
    }

    private static SpaceContext BuildContext(IServiceScope scope, Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        var accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = httpContext };
        return new SpaceContext(
            ApiFixture.Db(scope),
            scope.ServiceProvider.GetRequiredService<AioKin.Services.Common.Cache.IRedisService>(),
            scope.ServiceProvider.GetRequiredService<AioKin.Services.Family.IFamilyContext>(),
            accessor);
    }
}
```

This references `TestHttpContext.ForUser(...)` — check `AioKin.Tests/Infrastructure/` for an existing helper that builds a `ClaimsPrincipal`-bearing `HttpContext` for a given `UserUUID` (used by `FamilyContextTests.cs`, which tests `IFamilyContext` the same isolated way). If it doesn't exist yet, add it once in `AioKin.Tests/Infrastructure/TestHttpContext.cs`:

```csharp
using System.Security.Claims;
using AioKin.Common;
using Microsoft.AspNetCore.Http;

namespace AioKin.Tests.Infrastructure;

public static class TestHttpContext
{
    public static HttpContext ForUser(Guid userUuid)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userUuid.ToString())], "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~SpaceContextTests` → FAIL, `SpaceContext` does not exist.

- [ ] **Step 5: `SpaceContext.cs`**

```csharp
using AioKin.Common;
using AioKin.Data;
using AioKin.Data.Entities.Vault;
using AioKin.Services.Common.Cache;
using AioKin.Services.Family;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Vault;

public class SpaceContext : ISpaceContext
{
    private readonly AioKinDbContext _db;
    private readonly IRedisService _redis;
    private readonly IFamilyContext _familyContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SpaceContext(
        AioKinDbContext db,
        IRedisService redis,
        IFamilyContext familyContext,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _redis = redis;
        _familyContext = familyContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<SpaceMembership?> ResolveAsync(Guid spaceUuid, CancellationToken cancellationToken = default)
    {
        var userUuid = _httpContextAccessor.HttpContext?.User.GetUserUuid();
        if (userUuid is null)
            return null;

        var space = await _db.Spaces
            .AsNoTracking()
            .Include(s => s.Family)
            .FirstOrDefaultAsync(s => s.SpaceUUID == spaceUuid && s.IsActive, cancellationToken);

        if (space is null)
            return null;

        return space.SpaceType switch
        {
            SpaceType.Personal => ResolvePersonal(space, userUuid.Value),
            SpaceType.Family => await ResolveFamilyAsync(space, userUuid.Value, cancellationToken),
            SpaceType.Team => await ResolveTeamAsync(space, userUuid.Value, cancellationToken),
            _ => null
        };
    }

    private SpaceMembership? ResolvePersonal(Space space, Guid userUuid)
    {
        // Personal khong co bang thanh vien: chinh chu la thanh vien duy nhat. Can doi
        // OwnerUserID (noi bo) sang UserUUID de so sanh — tra ve null neu khong khop thay vi
        // truy van them, vi resolve that bai o day rat re (khong query nao ca).
        return _db.Users.AsNoTracking().Any(u => u.UserID == space.OwnerUserID && u.UserUUID == userUuid)
            ? new SpaceMembership(space.SpaceID, space.SpaceUUID, space.SpaceType, space.OwnerUserID, userUuid, CanManage: true)
            : null;
    }

    private async Task<SpaceMembership?> ResolveFamilyAsync(Space space, Guid userUuid, CancellationToken cancellationToken)
    {
        if (space.Family is null)
            return null;

        // Uy quyen hoan toan cho IFamilyContext — KHONG tu query family_members o day.
        var familyMembership = await _familyContext.ResolveAsync(space.Family.FamilyUUID, cancellationToken);
        if (familyMembership is null)
            return null;

        return new SpaceMembership(
            space.SpaceID, space.SpaceUUID, space.SpaceType,
            familyMembership.UserID, userUuid,
            CanManage: familyMembership.IsOwner);
    }

    private async Task<SpaceMembership?> ResolveTeamAsync(Space space, Guid userUuid, CancellationToken cancellationToken)
    {
        var row = await _db.SpaceMembers
            .AsNoTracking()
            .Where(m => m.SpaceID == space.SpaceID && m.User!.UserUUID == userUuid)
            .Select(m => new { m.UserID, m.MemberRole })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        return new SpaceMembership(
            space.SpaceID, space.SpaceUUID, space.SpaceType,
            row.UserID, userUuid,
            CanManage: row.MemberRole is SpaceMemberRole.Owner or SpaceMemberRole.Admin);
    }

    public Task InvalidateAsync(Guid spaceUuid, Guid userUuid, CancellationToken cancellationToken = default)
        // Space resolve khong cache rieng trong plan nay — no doc thang tu Spaces/SpaceMembers
        // (re) hoac uy quyen cho IFamilyContext (co cache rieng, tu invalidate qua duong cua
        // no). Giu ham nay de khop interface va de mo rong cache sau nay ma khong doi chu ky.
        => Task.CompletedTask;
}
```

- [ ] **Step 6: Register in `Program.cs`**

```csharp
builder.Services.AddScoped<ISpaceContext, SpaceContext>();
```

Add `using AioKin.Services.Vault;`.

- [ ] **Step 7: Run tests, commit**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~SpaceContextTests` → PASS.

```bash
git add -A
git commit -m "feat(vault): add ISpaceContext, delegating family membership to IFamilyContext"
```

---

### Task 3: `ISpaceService` — personal auto-create, family auto-link, team create/add-member

Before this task: run `impact({target: "FamilyService", direction: "upstream", repo: "AioKin"})` per `CLAUDE.md` and confirm the only change needed is inside `CreateAsync`'s existing transaction — this plan does not touch `JoinAsync` or `CreateInviteAsync`.

**Files:**
- Create: `AioKin/Services/Vault/ISpaceService.cs`, `SpaceService.cs`
- Modify: `AioKin/Services/Family/FamilyService.cs`
- Modify: `AioKin/Data/DbSeeder.cs` (backfill Space rows for families/users that predate this migration)
- Create: `AioKin.Tests/Vault/SpaceServiceTests.cs`

**Interfaces:**
- Produces: `ISpaceService.EnsureMyPersonalSpaceAsync(Guid callerUserUuid, CancellationToken)`, `CreateTeamAsync(Guid callerUserUuid, CreateTeamSpaceRequest, CancellationToken)`, `AddMemberAsync(Guid spaceUuid, AddSpaceMemberRequest, CancellationToken)`, `GetMineAsync(Guid callerUserUuid, CancellationToken)`.

- [ ] **Step 1: `ISpaceService.cs`**

```csharp
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Vault;
using AioKin.Models.ViewModel.Vault;

namespace AioKin.Services.Vault;

public interface ISpaceService
{
    /// <summary>Tra ve Space personal cua caller, tao moi neu chua co. Idempotent.</summary>
    Task<SpaceResponse> EnsureMyPersonalSpaceAsync(Guid callerUserUuid, CancellationToken cancellationToken = default);

    /// <summary>Tao Team space moi. Nguoi tao thanh Owner trong cung transaction.</summary>
    Task<OperationResult> CreateTeamAsync(Guid callerUserUuid, CreateTeamSpaceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Them thanh vien vao Team. Chi Owner/Admin goi duoc.</summary>
    Task<OperationResult> AddMemberAsync(Guid spaceUuid, AddSpaceMemberRequest request, CancellationToken cancellationToken = default);

    /// <summary>Moi space nguoi goi thuoc ve: personal (tu tao neu chua co) + family + team.</summary>
    Task<IReadOnlyList<SpaceResponse>> GetMineAsync(Guid callerUserUuid, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: DTOs**

`AioKin/Models/InputModel/Vault/CreateTeamSpaceRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Vault;

public class CreateTeamSpaceRequest
{
    [Required(AllowEmptyStrings = false)]
    [MinLength(1)]
    [MaxLength(120)]
    public required string Name { get; set; }
}
```

`AddSpaceMemberRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Vault;

public class AddSpaceMemberRequest
{
    /// <summary>UserCode cua nguoi duoc them — khong nhan UserUUID tu client de tranh do doan.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string UserCode { get; set; }
}
```

`AioKin/Models/ViewModel/Vault/SpaceResponse.cs`:
```csharp
using AioKin.Data.Entities.Vault;

namespace AioKin.Models.ViewModel.Vault;

public class SpaceResponse
{
    public Guid SpaceUuid { get; set; }
    public string SpaceType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool CanManage { get; set; }
    public long CreatedAtMillis { get; set; }

    public static SpaceResponse From(Space space, bool canManage) => new()
    {
        SpaceUuid = space.SpaceUUID,
        SpaceType = space.SpaceType.ToString(),
        Name = space.Name,
        CanManage = canManage,
        CreatedAtMillis = new DateTimeOffset(DateTime.SpecifyKind(space.CreatedDate, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
    };
}
```

- [ ] **Step 3: Write the failing tests**

```csharp
using AioKin.Data.Entities.Vault;
using AioKin.Models.InputModel.Vault;
using AioKin.Services.Vault;
using AioKin.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AioKin.Tests.Vault;

[Collection(ApiCollection.Name)]
public class SpaceServiceTests
{
    private readonly ApiFixture _fixture;

    public SpaceServiceTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task EnsureMyPersonalSpaceAsync_tao_dung_1_lan_du_goi_nhieu_lan()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = scope.ServiceProvider.GetRequiredService<ISpaceService>();

        var first = await sut.EnsureMyPersonalSpaceAsync(user.UserUUID);
        var second = await sut.EnsureMyPersonalSpaceAsync(user.UserUUID);

        Assert.Equal(first.SpaceUuid, second.SpaceUuid);
        Assert.Equal(1, await db.Spaces.CountAsync(s => s.OwnerUserID == user.UserID && s.SpaceType == SpaceType.Personal));
    }

    [Fact]
    public async Task CreateTeamAsync_nguoi_tao_thanh_Owner()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = scope.ServiceProvider.GetRequiredService<ISpaceService>();
        var result = await sut.CreateTeamAsync(user.UserUUID, new CreateTeamSpaceRequest { Name = "Team A" });

        Assert.True(result.Success);
        var space = await db.Spaces.FirstAsync(s => s.SpaceType == SpaceType.Team);
        var membership = await db.SpaceMembers.FirstAsync(m => m.SpaceID == space.SpaceID && m.UserID == user.UserID);
        Assert.Equal(SpaceMemberRole.Owner, membership.MemberRole);
    }
}
```

(`using Microsoft.EntityFrameworkCore;` for `CountAsync`/`FirstAsync`.)

- [ ] **Step 4: Run to verify failure**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~SpaceServiceTests` → FAIL.

- [ ] **Step 5: `SpaceService.cs`**

```csharp
using AioKin.Common;
using AioKin.Data;
using AioKin.Data.Entities.Vault;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Vault;
using AioKin.Models.ViewModel.Vault;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Vault;

public class SpaceService : ISpaceService
{
    private readonly AioKinDbContext _db;

    public SpaceService(AioKinDbContext db)
    {
        _db = db;
    }

    public async Task<SpaceResponse> EnsureMyPersonalSpaceAsync(Guid callerUserUuid, CancellationToken cancellationToken = default)
    {
        var userId = await _db.Users.Where(u => u.UserUUID == callerUserUuid).Select(u => u.UserID).FirstAsync(cancellationToken);

        var existing = await _db.Spaces.FirstOrDefaultAsync(
            s => s.OwnerUserID == userId && s.SpaceType == SpaceType.Personal, cancellationToken);

        if (existing is not null)
            return SpaceResponse.From(existing, canManage: true);

        var space = new Space { SpaceType = SpaceType.Personal, Name = "Personal", OwnerUserID = userId };

        try
        {
            _db.Spaces.Add(space);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Race: hai request cung tao personal space cung luc. Unique index (nam o Task 1
            // migration bo sung, xem Step 6) chan ban ghi thu hai — doc lai ban da thang.
            _db.ChangeTracker.Clear();
            existing = await _db.Spaces.FirstAsync(
                s => s.OwnerUserID == userId && s.SpaceType == SpaceType.Personal, cancellationToken);
            return SpaceResponse.From(existing, canManage: true);
        }

        return SpaceResponse.From(space, canManage: true);
    }

    public async Task<OperationResult> CreateTeamAsync(Guid callerUserUuid, CreateTeamSpaceRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await _db.Users.Where(u => u.UserUUID == callerUserUuid).Select(u => u.UserID).FirstOrDefaultAsync(cancellationToken);
        if (userId == Guid.Empty)
            return OperationResult.Fail("UserNotFound", "Khong tim thay tai khoan.");

        var space = new Space { SpaceType = SpaceType.Team, Name = request.Name.Trim(), OwnerUserID = userId };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Spaces.Add(space);
        _db.SpaceMembers.Add(new SpaceMember { SpaceID = space.SpaceID, UserID = userId, MemberRole = SpaceMemberRole.Owner });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OperationResult.Ok("Da tao team.", SpaceResponse.From(space, canManage: true));
    }

    public async Task<OperationResult> AddMemberAsync(Guid spaceUuid, AddSpaceMemberRequest request, CancellationToken cancellationToken = default)
    {
        var space = await _db.Spaces.FirstOrDefaultAsync(s => s.SpaceUUID == spaceUuid && s.SpaceType == SpaceType.Team, cancellationToken);
        if (space is null)
            return OperationResult.Fail("NotFound", "Khong tim thay team.");

        var target = await _db.Users.FirstOrDefaultAsync(u => u.UserCode == request.UserCode, cancellationToken);
        if (target is null)
            return OperationResult.Fail("UserNotFound", "Khong tim thay nguoi dung voi UserCode nay.");

        var alreadyMember = await _db.SpaceMembers.AnyAsync(m => m.SpaceID == space.SpaceID && m.UserID == target.UserID, cancellationToken);
        if (alreadyMember)
            return OperationResult.Fail("Conflict", "Nguoi nay da o trong team.");

        _db.SpaceMembers.Add(new SpaceMember { SpaceID = space.SpaceID, UserID = target.UserID, MemberRole = SpaceMemberRole.Member });
        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Ok("Da them thanh vien.");
    }

    public async Task<IReadOnlyList<SpaceResponse>> GetMineAsync(Guid callerUserUuid, CancellationToken cancellationToken = default)
    {
        await EnsureMyPersonalSpaceAsync(callerUserUuid, cancellationToken);

        var userId = await _db.Users.Where(u => u.UserUUID == callerUserUuid).Select(u => u.UserID).FirstAsync(cancellationToken);

        var owned = await _db.Spaces
            .AsNoTracking()
            .Where(s => s.IsActive && (
                s.OwnerUserID == userId ||
                _db.SpaceMembers.Any(m => m.SpaceID == s.SpaceID && m.UserID == userId) ||
                (s.FamilyID != null && _db.FamilyMembers.Any(fm => fm.FamilyID == s.FamilyID && fm.UserID == userId && fm.IsActive))
            ))
            .OrderByDescending(s => s.CreatedDate)
            .ToListAsync(cancellationToken);

        return [.. owned.Select(s => SpaceResponse.From(s, canManage: s.OwnerUserID == userId))];
    }
}
```

- [ ] **Step 6: Add the partial unique index for personal spaces**

Add to `AioKinDbContext.OnModelCreating`, inside the existing `modelBuilder.Entity<Space>(entity => { ... })` block from Task 1:

```csharp
            // 1 personal space moi user — chan race trong EnsureMyPersonalSpaceAsync o tang DB,
            // khong chi o tang application.
            entity.HasIndex(s => s.OwnerUserID)
                .IsUnique()
                .HasFilter("space_type = 0")
                .HasDatabaseName("ix_spaces_owner_personal_unique");
```

Regenerate: `dotnet ef migrations add AddSpacesPersonalUniqueIndex --output-dir Data/Migrations` (a second, small migration — simpler to review than folding it into Task 1's).

- [ ] **Step 7: Link Family creation to Space creation**

In `AioKin/Services/Family/FamilyService.cs`, inside `CreateAsync`'s existing transaction, right after `_db.FamilyMembers.Add(...)`:

```csharp
        _db.Spaces.Add(new AioKin.Data.Entities.Vault.Space
        {
            SpaceType = AioKin.Data.Entities.Vault.SpaceType.Family,
            Name = family.Name,
            OwnerUserID = userId,
            FamilyID = family.FamilyID
        });
```

(add `using AioKin.Data.Entities.Vault;` to the top of the file instead of the inline fully-qualified names, then simplify the two references above.)

- [ ] **Step 8: Backfill Space rows for families created before this migration**

In `AioKin/Data/DbSeeder.cs`, add a call in `SeedAsync` and the method itself:

```csharp
        await BackfillFamilySpacesAsync(db, logger);
```

```csharp
    /// <summary>
    /// Gia dinh tao truoc plan nay chua co Space tuong ung — Prompt domain se khong thay
    /// gia dinh do neu khong backfill. Idempotent: chi tao cho family chua co space.
    /// </summary>
    private static async Task BackfillFamilySpacesAsync(AioKinDbContext db, ILogger logger)
    {
        var missing = await db.Families
            .Where(f => f.IsActive && !db.Spaces.Any(s => s.FamilyID == f.FamilyID))
            .ToListAsync();

        if (missing.Count == 0)
            return;

        db.Spaces.AddRange(missing.Select(f => new Space
        {
            SpaceType = SpaceType.Family,
            Name = f.Name,
            OwnerUserID = f.OwnerUserID,
            FamilyID = f.FamilyID
        }));

        await db.SaveChangesAsync();
        logger.LogInformation("Backfilled {Count} family space(s).", missing.Count);
    }
```

Add `using AioKin.Data.Entities.Vault;` to `DbSeeder.cs`.

- [ ] **Step 9: Register `ISpaceService` in `Program.cs`**

```csharp
builder.Services.AddScoped<ISpaceService, SpaceService>();
```

- [ ] **Step 10: Run tests, full suite, commit**

Run: `dotnet test` → PASS (including `FamilyEndpointTests`/`FamilyContextTests` unchanged — this task only adds, never removes, behavior from `FamilyService.CreateAsync`).

```bash
git add -A
git commit -m "feat(vault): add SpaceService, link Family creation to a Space, backfill existing families"
```

---

### Task 4: `SpacesController`

**Files:**
- Create: `AioKin/Controllers/Vault/SpacesController.cs`
- Create: `AioKin.Tests/Vault/SpaceEndpointTests.cs`

**Interfaces:** consumes `ISpaceService` (Task 3).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using AioKin.Models.ViewModel.Vault;
using AioKin.Tests.Infrastructure;
using Xunit;

namespace AioKin.Tests.Vault;

[Collection(ApiCollection.Name)]
public class SpaceEndpointTests
{
    private readonly ApiFixture _fixture;

    public SpaceEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetMine_luon_co_it_nhat_1_personal_space()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var response = await testUser.Client.GetAsync("/spaces/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<List<SpaceResponse>>>();
        Assert.Contains(body!.Data!, s => s.SpaceType == "Personal");
    }

    [Fact]
    public async Task CreateTeam_roi_GetMine_thay_team_do()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var create = await testUser.Client.PostAsJsonAsync("/spaces/team", new { name = "Team A" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var mine = await testUser.Client.GetFromJsonAsync<OperationResultOf<List<SpaceResponse>>>("/spaces/me");
        Assert.Contains(mine!.Data!, s => s.Name == "Team A" && s.SpaceType == "Team");
    }
}
```

`OperationResultOf<T>` already exists from the token-session-management plan's tests — reuse it, don't redefine.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~SpaceEndpointTests` → FAIL, 404.

- [ ] **Step 3: `SpacesController.cs`**

```csharp
using AioKin.Common;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Vault;
using AioKin.Services.Vault;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Vault;

/// <summary>
/// Danh tinh LUON lay tu token. spaceUuid tren duong dan la space dang thao tac, khong
/// phai danh tinh nguoi goi.
/// </summary>
[ApiController]
[Route("spaces")]
[Produces("application/json")]
[Authorize(Roles = Roles.CUSTOMER)]
public class SpacesController : ControllerBase
{
    private readonly ISpaceService _spaceService;

    public SpacesController(ISpaceService spaceService)
    {
        _spaceService = spaceService;
    }

    /// <summary>Moi space nguoi goi thuoc ve — personal (tu tao neu chua co), family, team.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return this.ToActionResult(OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        return this.ToActionResult(OperationResult.Ok(data: await _spaceService.GetMineAsync(userUuid.Value, cancellationToken)));
    }

    /// <summary>Tao Team space moi. Nguoi tao thanh Owner.</summary>
    [HttpPost("team")]
    public async Task<IActionResult> CreateTeam([FromBody] CreateTeamSpaceRequest request, CancellationToken cancellationToken)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return this.ToActionResult(OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        return this.ToActionResult(await _spaceService.CreateTeamAsync(userUuid.Value, request, cancellationToken));
    }

    /// <summary>Them thanh vien vao Team bang UserCode.</summary>
    [HttpPost("{uuid:guid}/members")]
    public async Task<IActionResult> AddMember(Guid uuid, [FromBody] AddSpaceMemberRequest request, CancellationToken cancellationToken)
        => this.ToActionResult(await _spaceService.AddMemberAsync(uuid, request, cancellationToken));
}
```

- [ ] **Step 4: Run, commit**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~SpaceEndpointTests` → PASS.

```bash
git add -A
git commit -m "feat(vault): add SpacesController"
```

---

### Task 5: Prompt-domain entities (client-generated ids)

**Files:**
- Create: `AioKin/Data/Entities/Vault/Category.cs`, `Tag.cs`, `PromptTag.cs`, `Prompt.cs`, `PromptVariable.cs`
- Modify: `AioKin/Data/AioKinDbContext.cs`
- Modify: `AioKin/Data/DbSeeder.cs` (CASL rules)
- Create (via CLI): migration `AddPromptVaultDomain`

**Interfaces:**
- Produces: `Category`, `Tag`, `Prompt` (with tiered-storage + sync columns from the spec), `PromptVariable`, `PromptTag`, each with `public const string SubjectType`.

- [ ] **Step 1: `Category.cs` / `Tag.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Vault;

/// <summary>
/// Id client tu sinh (khong phai server) — xem Global Constraints cua plan nay: 4 entity
/// nay la nhom duy nhat can id on dinh truoc khi cham server, phuc vu offline sync.
/// </summary>
[Table("categories", Schema = "vault")]
public class Category
{
    public const string SubjectType = "Category";

    [Key]
    public Guid CategoryID { get; set; }

    public Guid SpaceID { get; set; }

    [ForeignKey(nameof(SpaceID))]
    public Space? Space { get; set; }

    [MaxLength(80)]
    public required string Name { get; set; }

    [MaxLength(50)]
    public string? Icon { get; set; }

    [MaxLength(20)]
    public string? Color { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
```

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Vault;

[Table("tags", Schema = "vault")]
public class Tag
{
    public const string SubjectType = "Tag";

    [Key]
    public Guid TagID { get; set; }

    public Guid SpaceID { get; set; }

    [ForeignKey(nameof(SpaceID))]
    public Space? Space { get; set; }

    [MaxLength(50)]
    public required string Name { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: `Prompt.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Data.Entities.Vault;

/// <summary>
/// Id client tu sinh. Version la base_version cho /sync/push — lech version khi push nghia
/// la conflict, khong tu Last-Write-Wins (xem spec muc 6.2). HasConflict la co dua vao
/// sync.sync_conflicts, khong tu suy ra o day.
/// </summary>
[Table("prompts", Schema = "vault")]
public class Prompt
{
    public const string SubjectType = "Prompt";

    [Key]
    public Guid PromptID { get; set; }

    public Guid SpaceID { get; set; }

    [ForeignKey(nameof(SpaceID))]
    public Space? Space { get; set; }

    public Guid? CategoryID { get; set; }

    [ForeignKey(nameof(CategoryID))]
    public Category? Category { get; set; }

    public Guid AuthorUserID { get; set; }

    [ForeignKey(nameof(AuthorUserID))]
    public UserDb? Author { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    public required string Content { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsFavorite { get; set; }
    public bool IsArchived { get; set; }
    public int UsageCount { get; set; }

    // --- Tiered storage (spec section 3.1's original draft, ported as-is) ---
    public int? ContentSizeBytes { get; set; }
    public bool IsExternalized { get; set; }
    [MaxLength(500)]
    public string? ContentStoragePath { get; set; }

    // --- Sync metadata ---
    public int Version { get; set; } = 1;
    public bool IsDeleted { get; set; }
    public bool HasConflict { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? UpdatedDeviceId { get; set; }

    public ICollection<PromptTag> PromptTags { get; set; } = [];
    public ICollection<PromptVariable> Variables { get; set; } = [];
}
```

- [ ] **Step 3: `PromptVariable.cs` / `PromptTag.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Vault;

[Table("prompt_variables", Schema = "vault")]
public class PromptVariable
{
    public const string SubjectType = "PromptVariable";

    [Key]
    public Guid VariableID { get; set; }

    public Guid PromptID { get; set; }

    [ForeignKey(nameof(PromptID))]
    public Prompt? Prompt { get; set; }

    [MaxLength(50)]
    public required string VarKey { get; set; }

    [MaxLength(100)]
    public string? Label { get; set; }

    [MaxLength(500)]
    public string? DefaultValue { get; set; }

    [MaxLength(20)]
    public string VarType { get; set; } = "text";

    /// <summary>JSON, chi dung khi VarType = "select": ["A","B","C"].</summary>
    public string? Options { get; set; }

    public int SortOrder { get; set; }
}
```

```csharp
namespace AioKin.Data.Entities.Vault;

/// <summary>
/// Nhieu-nhieu Prompt-Tag. KHONG phai mot entity_type rieng trong sync_log — dong bo nhu
/// mot phan cua payload Prompt (spec muc 6, sync_log.entity_type khong co 'prompt_tag').
/// </summary>
public class PromptTag
{
    public Guid PromptID { get; set; }
    public Prompt? Prompt { get; set; }

    public Guid TagID { get; set; }
    public Tag? Tag { get; set; }
}
```

- [ ] **Step 4: Wire into `AioKinDbContext.cs`**

```csharp
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Prompt> Prompts => Set<Prompt>();
    public DbSet<PromptVariable> PromptVariables => Set<PromptVariable>();
    public DbSet<PromptTag> PromptTags => Set<PromptTag>();
```

```csharp
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasIndex(c => c.SpaceID);
            entity.HasIndex(c => new { c.SpaceID, c.Name }).IsUnique();
            entity.HasOne(c => c.Space).WithMany().HasForeignKey(c => c.SpaceID).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasIndex(t => t.SpaceID);
            entity.HasIndex(t => new { t.SpaceID, t.Name }).IsUnique();
            entity.HasOne(t => t.Space).WithMany().HasForeignKey(t => t.SpaceID).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Prompt>(entity =>
        {
            entity.HasIndex(p => p.SpaceID);
            entity.HasIndex(p => p.CategoryID);
            entity.HasIndex(p => new { p.SpaceID, p.IsFavorite }).HasFilter("is_deleted = false");
            entity.HasIndex(p => new { p.SpaceID, p.UpdatedDate }).HasFilter("is_deleted = false");
            // FTS: y het thiet ke trong db/init-postgres.sql, sinh bang raw SQL trong migration
            // (Task 6 Step 2) vi HasGeneratedTsVectorColumn khong khop cach dung to_tsvector
            // truc tiep tren 2 cot ma khong luu them cot moi.

            entity.HasOne(p => p.Space).WithMany().HasForeignKey(p => p.SpaceID).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryID).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(p => p.Author).WithMany().HasForeignKey(p => p.AuthorUserID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PromptVariable>(entity =>
        {
            entity.HasIndex(v => v.PromptID);
            entity.HasIndex(v => new { v.PromptID, v.VarKey }).IsUnique();
            entity.HasOne(v => v.Prompt).WithMany(p => p.Variables).HasForeignKey(v => v.PromptID).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PromptTag>(entity =>
        {
            entity.HasKey(pt => new { pt.PromptID, pt.TagID });
            entity.HasOne(pt => pt.Prompt).WithMany(p => p.PromptTags).HasForeignKey(pt => pt.PromptID).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(pt => pt.Tag).WithMany().HasForeignKey(pt => pt.TagID).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(pt => pt.TagID);
        });
```

- [ ] **Step 5: CASL rules in `DbSeeder.cs`**

Append to `customerRules` in `SeedRolePermissionsAsync` (after the existing `FamilyInvite.SubjectType` line — appended, not reordered):

```csharp
              {"action":"manage","subject":"{{Space.SubjectType}}"},
              {"action":"manage","subject":"{{Prompt.SubjectType}}"},
              {"action":"manage","subject":"{{Category.SubjectType}}"},
              {"action":"manage","subject":"{{Tag.SubjectType}}"}
```

`"manage"` here is deliberately broader than Family's per-action rules: Prompt-domain access is already scoped per-request by `ISpaceContext` (space membership + `CanManage`/author checks happen in the service layer, same as `FamilyMember.SubjectType` uses a plain `"read"` rule while the real gate is `IFamilyContext`) — CASL only needs to confirm the *role* (Customer) may touch these subject types at all.

- [ ] **Step 6: Generate the migration**

```bash
cd AioKin
dotnet ef migrations add AddPromptVaultDomain --output-dir Data/Migrations
cd ..
```

Then hand-edit the generated migration's `Up()` to append the FTS index (EF can't express `to_tsvector` expression indexes declaratively):

```csharp
migrationBuilder.Sql(
    "CREATE INDEX ix_prompts_fts ON vault.prompts USING GIN (to_tsvector('simple', title || ' ' || content));");
```

and the matching `Down()`:

```csharp
migrationBuilder.Sql("DROP INDEX IF EXISTS vault.ix_prompts_fts;");
```

- [ ] **Step 7: Build, run full suite, commit**

Run: `dotnet build AioKin/AioKin.csproj && dotnet test` → both succeed.

```bash
git add -A
git commit -m "feat(vault): add Category, Tag, Prompt, PromptVariable, PromptTag entities"
```

---

### Task 6: Read-only browsing — `PromptsController`

**Files:**
- Create: `AioKin/Services/Vault/IPromptBrowseService.cs`, `PromptBrowseService.cs`
- Create: `AioKin/Models/ViewModel/Vault/PromptSummaryResponse.cs`, `PromptDetailResponse.cs`, `CategoryResponse.cs`, `TagResponse.cs`
- Create: `AioKin/Controllers/Vault/PromptsController.cs`
- Create: `AioKin.Tests/Vault/PromptBrowseEndpointTests.cs`

**Interfaces:** consumes `ISpaceContext` (Task 2).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using AioKin.Data.Entities.Vault;
using AioKin.Models.ViewModel.Vault;
using AioKin.Tests.Infrastructure;
using Xunit;

namespace AioKin.Tests.Vault;

[Collection(ApiCollection.Name)]
public class PromptBrowseEndpointTests
{
    private readonly ApiFixture _fixture;

    public PromptBrowseEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Liet_ke_prompt_trong_personal_space_cua_chinh_minh()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var mine = await testUser.Client.GetFromJsonAsync<OperationResultOf<System.Collections.Generic.List<SpaceResponse>>>("/spaces/me");
        var personalSpaceUuid = mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;

        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == personalSpaceUuid);
            db.Prompts.Add(new Prompt
            {
                PromptID = Guid.NewGuid(),
                SpaceID = space.SpaceID,
                AuthorUserID = testUser.UserId,
                Title = "Caption skincare",
                Content = "Viet caption quang cao san pham skincare"
            });
            await db.SaveChangesAsync();
        }

        var response = await testUser.Client.GetAsync($"/prompts?spaceUuid={personalSpaceUuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<System.Collections.Generic.List<PromptSummaryResponse>>>();
        Assert.Single(body!.Data!);
        Assert.Equal("Caption skincare", body.Data![0].Title);
    }

    [Fact]
    public async Task Nguoi_ngoai_space_bi_tu_choi()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var stranger = await TestUser.CreateAsync(_fixture);

        var mine = await owner.Client.GetFromJsonAsync<OperationResultOf<System.Collections.Generic.List<SpaceResponse>>>("/spaces/me");
        var personalSpaceUuid = mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;

        var response = await stranger.Client.GetAsync($"/prompts?spaceUuid={personalSpaceUuid}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~PromptBrowseEndpointTests` → FAIL.

- [ ] **Step 3: DTOs**

```csharp
namespace AioKin.Models.ViewModel.Vault;

public class PromptSummaryResponse
{
    public Guid PromptId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsFavorite { get; set; }
    public bool HasConflict { get; set; }
    public long UpdatedAtMillis { get; set; }
}

public class PromptDetailResponse
{
    public Guid PromptId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; }
    public bool HasConflict { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<PromptVariableResponse> Variables { get; set; } = [];
}

public class PromptVariableResponse
{
    public string VarKey { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? DefaultValue { get; set; }
}
```

(`CategoryResponse`/`TagResponse` are trivial `{ Id, Name }` shapes — write them the same way, one file each.)

- [ ] **Step 4: `IPromptBrowseService.cs` / `PromptBrowseService.cs`**

```csharp
using AioKin.Models.ViewModel.Vault;

namespace AioKin.Services.Vault;

public interface IPromptBrowseService
{
    Task<OperationResultEnvelope<IReadOnlyList<PromptSummaryResponse>>> ListAsync(Guid spaceUuid, Guid callerUserUuid, CancellationToken cancellationToken = default);
    Task<OperationResultEnvelope<PromptDetailResponse>> GetAsync(Guid spaceUuid, Guid promptId, Guid callerUserUuid, CancellationToken cancellationToken = default);
}
```

Rather than invent a new envelope type, use the existing `AioKin.Models.InputModel.Auth.User.OperationResult` directly (it already carries `Data` as `object?`) — drop the `OperationResultEnvelope<T>` generic above and have both methods return `Task<OperationResult>`, matching every other service in the codebase:

```csharp
using AioKin.Data;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Vault;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Vault;

public class PromptBrowseService : IPromptBrowseService
{
    private readonly AioKinDbContext _db;
    private readonly ISpaceContext _spaceContext;

    public PromptBrowseService(AioKinDbContext db, ISpaceContext spaceContext)
    {
        _db = db;
        _spaceContext = spaceContext;
    }

    public async Task<OperationResult> ListAsync(Guid spaceUuid, CancellationToken cancellationToken = default)
    {
        var membership = await _spaceContext.ResolveAsync(spaceUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("Forbidden", "Ban khong thuoc space nay.");

        var prompts = await _db.Prompts
            .AsNoTracking()
            .Where(p => p.SpaceID == membership.SpaceID && !p.IsDeleted)
            .OrderByDescending(p => p.UpdatedDate)
            .Select(p => new PromptSummaryResponse
            {
                PromptId = p.PromptID,
                Title = p.Title,
                Description = p.Description,
                IsFavorite = p.IsFavorite,
                HasConflict = p.HasConflict,
                UpdatedAtMillis = new DateTimeOffset(DateTime.SpecifyKind(p.UpdatedDate, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
            })
            .ToListAsync(cancellationToken);

        return OperationResult.Ok(data: prompts);
    }

    public async Task<OperationResult> GetAsync(Guid spaceUuid, Guid promptId, CancellationToken cancellationToken = default)
    {
        var membership = await _spaceContext.ResolveAsync(spaceUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("Forbidden", "Ban khong thuoc space nay.");

        var prompt = await _db.Prompts
            .AsNoTracking()
            .Include(p => p.Variables)
            .Include(p => p.PromptTags).ThenInclude(pt => pt.Tag)
            .FirstOrDefaultAsync(p => p.SpaceID == membership.SpaceID && p.PromptID == promptId && !p.IsDeleted, cancellationToken);

        if (prompt is null)
            return OperationResult.Fail("NotFound", "Khong tim thay prompt.");

        return OperationResult.Ok(data: new PromptDetailResponse
        {
            PromptId = prompt.PromptID,
            Title = prompt.Title,
            Content = prompt.Content,
            Description = prompt.Description,
            Version = prompt.Version,
            HasConflict = prompt.HasConflict,
            Tags = [.. prompt.PromptTags.Select(pt => pt.Tag!.Name)],
            Variables = [.. prompt.Variables.Select(v => new PromptVariableResponse { VarKey = v.VarKey, Label = v.Label, DefaultValue = v.DefaultValue })]
        });
    }
}
```

(update `IPromptBrowseService` to match this simpler `Task<OperationResult>` signature, dropping `callerUserUuid` — identity is already folded into `ISpaceContext.ResolveAsync` via `IHttpContextAccessor`, so the service methods don't need it as a parameter, matching how `FamilyService.CreateInviteAsync` doesn't take a caller id either.)

- [ ] **Step 5: `PromptsController.cs`**

```csharp
using AioKin.Common;
using AioKin.Services.Vault;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Vault;

/// <summary>
/// Chi doc. Tao/sua/xoa prompt di qua /sync/push (xem
/// docs/superpowers/plans/2026-09-25-promptvault-sync-engine.md) — khong co POST/PUT/DELETE
/// o day, tranh mo mot duong ghi song song bo qua version tracking.
/// </summary>
[ApiController]
[Route("prompts")]
[Produces("application/json")]
[Authorize(Roles = Roles.CUSTOMER)]
public class PromptsController : ControllerBase
{
    private readonly IPromptBrowseService _browseService;

    public PromptsController(IPromptBrowseService browseService)
    {
        _browseService = browseService;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid spaceUuid, CancellationToken cancellationToken)
        => this.ToActionResult(await _browseService.ListAsync(spaceUuid, cancellationToken));

    [HttpGet("{promptId:guid}")]
    public async Task<IActionResult> Get([FromQuery] Guid spaceUuid, Guid promptId, CancellationToken cancellationToken)
        => this.ToActionResult(await _browseService.GetAsync(spaceUuid, promptId, cancellationToken));
}
```

- [ ] **Step 6: Register in `Program.cs`, run tests, run `detect_changes()`, commit**

```csharp
builder.Services.AddScoped<IPromptBrowseService, PromptBrowseService>();
```

Run: `dotnet test` → PASS. Run `detect_changes({scope: "compare", base_ref: "feat/family-core"})` and confirm the affected files match this plan's File Structure table.

```bash
git add -A
git commit -m "feat(vault): add read-only PromptsController"
```

---

## After this plan

`Space`, `Prompt`, `Category`, `Tag`, `PromptVariable` exist, are readable, and are correctly scoped by membership through `ISpaceContext`. No client can create, edit, or delete a prompt yet — that ships in `docs/superpowers/plans/2026-09-25-promptvault-sync-engine.md`, which adds `/sync/push`, `/sync/pull`, and `/sync/conflicts/{id}/resolve` as the sole mutation path for these five entities.
