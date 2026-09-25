# PromptVault — Sync Engine & Conflict Resolution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `/sync/push`, `/sync/pull`, `/sync/conflicts/{id}/resolve` — the sole mutation path for `Prompt` (create/update/delete), with Git-style conflict detection (no automatic Last-Write-Wins) and a hybrid pull strategy that falls back to a full snapshot when incremental replay would be wrong or wasteful.

**Architecture:** Every push carries a `baseVersion` per prompt. A clean push (`baseVersion == current version`) applies via EF's own optimistic-concurrency check (`Prompt.Version` as a concurrency token) — this catches the race between two pushes landing at nearly the same instant, on top of an explicit application-level version comparison that catches the common case (an offline edit whose base is stale by minutes or days). Either mismatch produces the same outcome: **no write to the live row**, a `sync.sync_conflicts` record holding both payloads, `Prompt.HasConflict = true`. Pull streams `sync.sync_log` for the common case; when a cursor predates what the retention cron still keeps, or the incremental result would be large, it serves a compressed full-space snapshot from object storage instead (`sync.backup_snapshots`, `snapshot_type = 'sync_catchup'`).

**Scope narrowing from the spec, made explicit here:** the spec's `sync_log.entity_type` check constraint lists `'prompt' | 'category' | 'tag' | 'prompt_variable'`, but only `Prompt` gets independent conflict tracking in this plan. `Category` and `Tag` are resolved *inside* a prompt push (upsert by client-generated id + name, created on the fly if the id doesn't exist yet in that space) rather than pushed as their own top-level operations — they're low-conflict-risk labels, and giving them their own conflict machinery with no version/HasConflict columns of their own (the draft SQL never gave them any) would mean inventing an under-specified second conflict system. `PromptVariable` rows are replaced wholesale on every prompt update (delete-then-reinsert the set), matching how they're edited in practice (a prompt's variable list is edited as a unit, not variable-by-variable).

**Tech Stack:** .NET 9, EF Core 9 (optimistic concurrency via `IsConcurrencyToken()`), Npgsql, `IRedisService` unused here (pull/push are pure Postgres — no Redis-cached state in this plan), object storage via a new minimal `IBlobStorageService` (Supabase Storage), xUnit + `ApiFixture`.

**Spec:** `docs/superpowers/specs/2026-09-25-promptvault-merge-design.md` — section 6 (sync & conflict), section 3.3 (trigger fix), section 6.5 (device unification).

## Global Constraints

- **Depends on `docs/superpowers/plans/2026-09-25-promptvault-space-and-prompt-domain.md`** — `Prompt`, `Category`, `Tag`, `PromptVariable`, `ISpaceContext` must exist exactly as that plan leaves them before starting here.
- **No timestamp is ever compared to decide a sync outcome.** Every version mismatch is a conflict, full stop — this is what makes the design immune to client clock skew (a concern raised and deliberately designed away, not merely deferred).
- **The live `Prompt` row is never written when a conflict is detected.** Both payloads go to `sync.sync_conflicts`; the row keeps whatever was there before the conflicting push arrived.
- **Comments and docs are Vietnamese without diacritics** ("khong dau").
- **If the 3 auth plans (`2026-09-25-opaque-access-tokens.md` etc.) are implemented before this one**, `sync.devices` becomes the canonical device table referenced by `security.device_credentials` too (spec section 6.5) — this plan creates `sync.devices` either way; wiring the biometric plan's FK to it is a one-line follow-up in whichever plan lands second, not duplicated work here.
- **Run `detect_changes()` before the final commit.**

## Review Focus

- **Two devices pushing the same prompt from the same stale base concurrently** (a genuine race, not just sequential offline edits) must both be handled safely — one applies, the other either conflicts cleanly or hits the EF concurrency exception and is converted into a conflict record, never a 500 and never a silent double-apply.
- **A push for a `promptId` the server has never seen, sent as `operation: "update"`** (client retried an insert whose first response was lost) must not throw — insert is idempotent by id.
- **Resolving a conflict with `keep_remote`** must still clear `HasConflict` and leave the row exactly as it already was — a no-op content-wise, but the bookkeeping (resolved flag, cleared conflict flag) must complete.
- **A pull whose `since` is older than every row in `sync_log` for that space** — including the "no rows at all, and `since` is old" edge case — must fall back to a snapshot rather than silently returning an empty (and wrong) incremental result.
- **A category or tag referenced by name in a push, where a same-named one already exists in that space**, must reuse the existing row (by client-sent id if it matches, otherwise the plan's category/tag resolution must not silently create a duplicate with a different id than what other devices already know).

---

## Prerequisites

```bash
git checkout feat/promptvault-space-and-prompt-domain
git pull
git checkout -b feat/promptvault-sync-engine
```

---

## File Structure

**New:**

| File | Responsibility |
|---|---|
| `AioKin/Data/Entities/Sync/Device.cs` | Canonical device registry |
| `AioKin/Data/Entities/Sync/SyncLogEntry.cs` | Append-only change feed |
| `AioKin/Data/Entities/Sync/SyncConflict.cs` | Both payloads when a push doesn't fast-forward |
| `AioKin/Data/Entities/Sync/BackupSnapshot.cs` | Manual/scheduled/catchup space exports |
| `AioKin/Services/Common/Storage/IBlobStorageService.cs` | Minimal upload/download, gzip — reused later for tiered prompt-content storage |
| `AioKin/Services/Common/Storage/SupabaseStorageService.cs` | Implementation |
| `AioKin/Services/Vault/ISyncService.cs` | Push, pull, resolve |
| `AioKin/Services/Vault/SyncService.cs` | Implementation |
| `AioKin/Models/InputModel/Vault/SyncPushRequest.cs` | `PushPromptEntry`, `PromptPayload`, `PromptVariablePayload` |
| `AioKin/Models/InputModel/Vault/ResolveConflictRequest.cs` | |
| `AioKin/Models/ViewModel/Vault/SyncPushResponse.cs`, `SyncPullResponse.cs` | |
| `AioKin/Controllers/Vault/SyncController.cs` | `POST /sync/push`, `GET /sync/pull`, `POST /sync/conflicts/{id}/resolve` |
| `AioKin.Tests/Vault/SyncPushTests.cs`, `SyncPullTests.cs`, `SyncConflictResolveTests.cs` | |

**Modified:**

| File | Change |
|---|---|
| `AioKin/Data/AioKinDbContext.cs` | New `DbSet`s, `Prompt.Version` as concurrency token |
| `AioKin/Program.cs` | DI registration |

---

### Task 1: Sync entities, migration, and the two corrected triggers

**Files:**
- Create: `AioKin/Data/Entities/Sync/Device.cs`, `SyncLogEntry.cs`, `SyncConflict.cs`, `BackupSnapshot.cs`
- Modify: `AioKin/Data/AioKinDbContext.cs`
- Create (via CLI + hand-edit): migration `AddSyncEngine`

**Interfaces:**
- Produces: `Device`, `SyncLogEntry`, `SyncConflict`, `BackupSnapshot` entities; `Prompt.Version` configured as an EF concurrency token.

- [ ] **Step 1: Entities**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Sync;

/// <summary>
/// So dang ky thiet bi duy nhat. Neu 3 plan auth (opaque token/session/biometric) da chay,
/// security.device_credentials nen tro ve day thay vi tu luu ten/nen tang rieng — xem spec
/// muc 6.5. Plan nay tu dung duoc du plan auth chua chay.
/// </summary>
[Table("devices", Schema = "sync")]
public class Device
{
    [Key]
    [MaxLength(100)]
    public required string DeviceID { get; set; }

    public Guid UserID { get; set; }

    [MaxLength(120)]
    public string? DeviceName { get; set; }

    [MaxLength(20)]
    public string? Platform { get; set; }

    [MaxLength(20)]
    public string? AppVersion { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    public bool IsStale { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
```

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Sync;

[Table("sync_log", Schema = "sync")]
public class SyncLogEntry
{
    [Key]
    public long SyncLogID { get; set; }

    public Guid SpaceID { get; set; }

    [MaxLength(30)]
    public required string EntityType { get; set; }

    public Guid EntityID { get; set; }

    [MaxLength(10)]
    public required string Operation { get; set; }

    [Column(TypeName = "jsonb")]
    public string? PayloadJson { get; set; }

    [MaxLength(100)]
    public string? OriginDeviceId { get; set; }

    public int Version { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Sync;

/// <summary>
/// KHONG dung Last-Write-Wins. Ca 2 ban duoc giu — user tu chon keep_local/keep_remote/merged
/// qua /sync/conflicts/{id}/resolve. Xem spec muc 6.2-6.3.
/// </summary>
[Table("sync_conflicts", Schema = "sync")]
public class SyncConflict
{
    [Key]
    public Guid ConflictID { get; set; } = Guid.NewGuid();

    [MaxLength(30)]
    public required string EntityType { get; set; }

    public Guid EntityID { get; set; }

    [Column(TypeName = "jsonb")]
    public required string LocalPayloadJson { get; set; }

    [Column(TypeName = "jsonb")]
    public required string RemotePayloadJson { get; set; }

    public int LocalVersion { get; set; }
    public int RemoteVersion { get; set; }
    public bool Resolved { get; set; }

    [MaxLength(20)]
    public string? ResolutionStrategy { get; set; }

    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Sync;

[Table("backup_snapshots", Schema = "sync")]
public class BackupSnapshot
{
    [Key]
    public Guid SnapshotID { get; set; } = Guid.NewGuid();

    public Guid SpaceID { get; set; }
    public Guid TriggeredByUserID { get; set; }

    /// <summary>'manual' | 'scheduled' | 'pre_sync' | 'sync_catchup'.</summary>
    [MaxLength(20)]
    public required string SnapshotType { get; set; }

    [MaxLength(500)]
    public required string StoragePath { get; set; }

    public long? FileSizeBytes { get; set; }
    public int? PromptCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Wire into `AioKinDbContext.cs`**

```csharp
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<SyncLogEntry> SyncLog => Set<SyncLogEntry>();
    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();
    public DbSet<BackupSnapshot> BackupSnapshots => Set<BackupSnapshot>();
```

```csharp
        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasIndex(d => d.UserID);
            entity.HasIndex(d => d.LastSyncedAt).HasFilter("is_stale = false");
        });

        modelBuilder.Entity<SyncLogEntry>(entity =>
        {
            entity.Property(e => e.SyncLogID).ValueGeneratedOnAdd();
            entity.HasIndex(e => new { e.SpaceID, e.CreatedAt });
            entity.HasIndex(e => new { e.EntityType, e.EntityID });
        });

        modelBuilder.Entity<SyncConflict>(entity =>
        {
            entity.HasIndex(c => new { c.EntityType, c.EntityID }).HasFilter("resolved = false");
        });

        modelBuilder.Entity<BackupSnapshot>(entity =>
        {
            entity.HasIndex(s => new { s.SpaceID, s.CreatedAt }).IsDescending(false, true);
        });

        // Version la base_version cho /sync/push: EF tu them "WHERE version = @original" vao
        // UPDATE va nem DbUpdateConcurrencyException neu 0 dong bi anh huong — bat dung race
        // giua 2 push gan nhu cung luc, la lop phong thu THU HAI ben canh so sanh BaseVersion
        // tuong minh trong SyncService (xem Task 2).
        modelBuilder.Entity<AioKin.Data.Entities.Vault.Prompt>()
            .Property(p => p.Version)
            .IsConcurrencyToken();
```

- [ ] **Step 3: Generate the migration, then hand-edit for the two triggers**

```bash
cd AioKin
dotnet ef migrations add AddSyncEngine --output-dir Data/Migrations
cd ..
```

Append to the generated migration's `Up()` — these are the two triggers from `db/init-postgres.sql`, corrected per the spec (scoped `WHEN` clause on the version-bump trigger, so toggling `has_conflict`/`is_favorite` never bumps `version` or writes a spurious `sync_log` row):

```csharp
migrationBuilder.Sql("""
    CREATE OR REPLACE FUNCTION vault.fn_prompts_before_update()
    RETURNS TRIGGER AS $$
    BEGIN
        NEW.version := OLD.version + 1;
        NEW.updated_date := now();
        RETURN NEW;
    END;
    $$ LANGUAGE plpgsql;

    CREATE TRIGGER trg_prompts_before_update
        BEFORE UPDATE ON vault.prompts
        FOR EACH ROW
        WHEN (
            OLD.title IS DISTINCT FROM NEW.title OR
            OLD.content IS DISTINCT FROM NEW.content OR
            OLD.description IS DISTINCT FROM NEW.description OR
            OLD.category_id IS DISTINCT FROM NEW.category_id
        )
        EXECUTE FUNCTION vault.fn_prompts_before_update();

    CREATE OR REPLACE FUNCTION sync.fn_prompts_write_log()
    RETURNS TRIGGER AS $$
    BEGIN
        INSERT INTO sync.sync_log (space_id, entity_type, entity_id, operation, payload, origin_device_id, version)
        VALUES (
            COALESCE(NEW.space_id, OLD.space_id),
            'prompt',
            COALESCE(NEW.prompt_id, OLD.prompt_id),
            LOWER(TG_OP),
            CASE WHEN TG_OP = 'DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END,
            COALESCE(NEW.updated_device_id, OLD.updated_device_id),
            COALESCE(NEW.version, OLD.version)
        );
        RETURN COALESCE(NEW, OLD);
    END;
    $$ LANGUAGE plpgsql;

    CREATE TRIGGER trg_prompts_write_sync_log
        AFTER INSERT OR UPDATE OR DELETE ON vault.prompts
        FOR EACH ROW
        EXECUTE FUNCTION sync.fn_prompts_write_log();
    """);
```

and the matching `Down()`:

```csharp
migrationBuilder.Sql("""
    DROP TRIGGER IF EXISTS trg_prompts_write_sync_log ON vault.prompts;
    DROP FUNCTION IF EXISTS sync.fn_prompts_write_log();
    DROP TRIGGER IF EXISTS trg_prompts_before_update ON vault.prompts;
    DROP FUNCTION IF EXISTS vault.fn_prompts_before_update();
    """);
```

**Do not** additionally increment `prompt.Version` in C# anywhere in `SyncService` (Task 2) — the trigger owns it exclusively. A double-increment (trigger + C#) would make every push look like it landed on the wrong version.

- [ ] **Step 4: Build, run full suite, commit**

Run: `dotnet build AioKin/AioKin.csproj && dotnet test` → both succeed (no behavior changed yet — this task is pure schema).

```bash
git add -A
git commit -m "feat(sync): add sync engine entities, corrected version-bump and change-log triggers"
```

---

### Task 2: `POST /sync/push`

**Files:**
- Create: `AioKin/Models/InputModel/Vault/SyncPushRequest.cs`, `AioKin/Models/ViewModel/Vault/SyncPushResponse.cs`
- Create: `AioKin/Services/Vault/ISyncService.cs`, `SyncService.cs` (push half; pull/resolve added in Tasks 3-4)
- Create: `AioKin/Controllers/Vault/SyncController.cs`
- Create: `AioKin.Tests/Vault/SyncPushTests.cs`

**Interfaces:**
- Consumes: `ISpaceContext` (previous plan).
- Produces: `ISyncService.PushAsync(SyncPushRequest, CancellationToken)`.

- [ ] **Step 1: DTOs**

```csharp
namespace AioKin.Models.InputModel.Vault;

public class SyncPushRequest
{
    public required Guid SpaceUuid { get; set; }
    public string? DeviceId { get; set; }
    public required List<PushPromptEntry> Entities { get; set; }
}

public class PushPromptEntry
{
    /// <summary>Sinh boi client, dung nguyen lam id vinh vien — xem Global Constraints cua plan truoc.</summary>
    public required Guid PromptId { get; set; }

    /// <summary>"insert" | "update" | "delete".</summary>
    public required string Operation { get; set; }

    /// <summary>Version client biet luc bat dau sua. Bo qua khi Operation = "insert".</summary>
    public int BaseVersion { get; set; }

    /// <summary>Null khi Operation = "delete".</summary>
    public PromptPayload? Payload { get; set; }
}

public class PromptPayload
{
    public required string Title { get; set; }
    public required string Content { get; set; }
    public string? Description { get; set; }

    /// <summary>Ca hai deu tu client sinh khi tao category moi ngay trong luc sua prompt.</summary>
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }

    public List<TagRef> Tags { get; set; } = [];
    public List<PromptVariablePayload> Variables { get; set; } = [];
}

public class TagRef
{
    public required Guid TagId { get; set; }
    public required string Name { get; set; }
}

public class PromptVariablePayload
{
    public required Guid VariableId { get; set; }
    public required string VarKey { get; set; }
    public string? Label { get; set; }
    public string? DefaultValue { get; set; }
    public string VarType { get; set; } = "text";
}
```

```csharp
namespace AioKin.Models.ViewModel.Vault;

public class SyncPushResponse
{
    public Guid PromptId { get; set; }

    /// <summary>"applied" | "conflict".</summary>
    public string Status { get; set; } = string.Empty;

    public int? NewVersion { get; set; }

    /// <summary>Chi co gia tri khi Status = "conflict" — ban hien tai tren server.</summary>
    public PromptDetailResponse? Remote { get; set; }

    public Guid? ConflictId { get; set; }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using AioKin.Data.Entities.Vault;
using AioKin.Models.ViewModel.Vault;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Vault;

[Collection(ApiCollection.Name)]
public class SyncPushTests
{
    private readonly ApiFixture _fixture;

    public SyncPushTests(ApiFixture fixture) => _fixture = fixture;

    private async Task<Guid> GetPersonalSpaceUuidAsync(TestUser user)
    {
        var mine = await user.Client.GetFromJsonAsync<OperationResultOf<List<SpaceResponse>>>("/spaces/me");
        return mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;
    }

    [Fact]
    public async Task Insert_moi_thanh_cong_voi_id_client_sinh()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();

        var response = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            deviceId = "device-a",
            entities = new[] { new
            {
                promptId,
                operation = "insert",
                baseVersion = 0,
                payload = new { title = "Caption skincare", content = "Viet caption {product_name}", tags = Array.Empty<object>(), variables = Array.Empty<object>() }
            }}
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<List<SyncPushResponse>>>();
        Assert.Equal("applied", body!.Data![0].Status);
        Assert.Equal(1, body.Data![0].NewVersion);
    }

    [Fact]
    public async Task Update_dung_baseVersion_thi_ap_dung_va_tang_version()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();

        await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            deviceId = "device-a",
            entities = new[] { new { promptId, operation = "insert", baseVersion = 0,
                payload = new { title = "V1", content = "noi dung v1", tags = Array.Empty<object>(), variables = Array.Empty<object>() } } }
        });

        var response = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            deviceId = "device-a",
            entities = new[] { new { promptId, operation = "update", baseVersion = 1,
                payload = new { title = "V2", content = "noi dung v2", tags = Array.Empty<object>(), variables = Array.Empty<object>() } } }
        });

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<List<SyncPushResponse>>>();
        Assert.Equal("applied", body!.Data![0].Status);
        Assert.Equal(2, body.Data![0].NewVersion);
    }

    [Fact]
    public async Task Update_sai_baseVersion_thi_tra_conflict_khong_dung_live_row()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();

        await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            deviceId = "device-a",
            entities = new[] { new { promptId, operation = "insert", baseVersion = 0,
                payload = new { title = "V1", content = "noi dung goc", tags = Array.Empty<object>(), variables = Array.Empty<object>() } } }
        });

        // Device khac day version len 2 truoc.
        await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            deviceId = "device-b",
            entities = new[] { new { promptId, operation = "update", baseVersion = 1,
                payload = new { title = "V2 tu device B", content = "noi dung device B", tags = Array.Empty<object>(), variables = Array.Empty<object>() } } }
        });

        // Device A push tiep voi baseVersion = 1 (cu) — phai conflict.
        var response = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            deviceId = "device-a",
            entities = new[] { new { promptId, operation = "update", baseVersion = 1,
                payload = new { title = "V2 tu device A", content = "noi dung device A", tags = Array.Empty<object>(), variables = Array.Empty<object>() } } }
        });

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<List<SyncPushResponse>>>();
        Assert.Equal("conflict", body!.Data![0].Status);
        Assert.Equal("V2 tu device B", body.Data![0].Remote!.Title);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.Equal("V2 tu device B", live.Title);
        Assert.True(live.HasConflict);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~SyncPushTests` → FAIL, 404.

- [ ] **Step 4: `ISyncService.cs`**

```csharp
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Vault;

namespace AioKin.Services.Vault;

public interface ISyncService
{
    Task<OperationResult> PushAsync(SyncPushRequest request, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: `SyncService.cs` (push half)**

```csharp
using AioKin.Data;
using AioKin.Data.Entities.Vault;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Vault;
using AioKin.Models.ViewModel.Vault;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Vault;

public class SyncService : ISyncService
{
    private readonly AioKinDbContext _db;
    private readonly ISpaceContext _spaceContext;

    public SyncService(AioKinDbContext db, ISpaceContext spaceContext)
    {
        _db = db;
        _spaceContext = spaceContext;
    }

    public async Task<OperationResult> PushAsync(SyncPushRequest request, CancellationToken cancellationToken = default)
    {
        var membership = await _spaceContext.ResolveAsync(request.SpaceUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("Forbidden", "Ban khong thuoc space nay.");

        await TouchDeviceAsync(membership.UserID, request.DeviceId, cancellationToken);

        var results = new List<SyncPushResponse>();
        foreach (var entry in request.Entities)
            results.Add(await PushOneAsync(membership, request.DeviceId, entry, cancellationToken));

        return OperationResult.Ok(data: results);
    }

    private async Task TouchDeviceAsync(Guid userId, string? deviceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return;

        var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceID == deviceId, cancellationToken);
        if (device is null)
        {
            _db.Devices.Add(new AioKin.Data.Entities.Sync.Device { DeviceID = deviceId, UserID = userId, LastSyncedAt = DateTime.UtcNow });
        }
        else
        {
            device.LastSyncedAt = DateTime.UtcNow;
            device.IsStale = false;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<SyncPushResponse> PushOneAsync(SpaceMembership membership, string? deviceId, PushPromptEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Operation == "delete")
            return await PushDeleteAsync(membership, entry, cancellationToken);

        var existing = await _db.Prompts.FirstOrDefaultAsync(p => p.PromptID == entry.PromptId && p.SpaceID == membership.SpaceID, cancellationToken);

        if (existing is null)
            return await PushInsertAsync(membership, deviceId, entry, cancellationToken);

        return await PushUpdateAsync(membership, deviceId, existing, entry, cancellationToken);
    }

    private async Task<SyncPushResponse> PushInsertAsync(SpaceMembership membership, string? deviceId, PushPromptEntry entry, CancellationToken cancellationToken)
    {
        var (categoryId, tagIds) = await ResolveCategoryAndTagsAsync(membership.SpaceID, entry.Payload!, cancellationToken);

        var prompt = new Prompt
        {
            PromptID = entry.PromptId,
            SpaceID = membership.SpaceID,
            AuthorUserID = membership.UserID,
            CategoryID = categoryId,
            Title = entry.Payload!.Title,
            Content = entry.Payload.Content,
            Description = entry.Payload.Description,
            Version = 1,
            UpdatedDeviceId = deviceId
        };
        prompt.Variables = [.. entry.Payload.Variables.Select(v => new PromptVariable
        {
            VariableID = v.VariableId, PromptID = prompt.PromptID, VarKey = v.VarKey, Label = v.Label, DefaultValue = v.DefaultValue, VarType = v.VarType
        })];
        prompt.PromptTags = [.. tagIds.Select(id => new PromptTag { PromptID = prompt.PromptID, TagID = id })];

        _db.Prompts.Add(prompt);
        await _db.SaveChangesAsync(cancellationToken);

        return new SyncPushResponse { PromptId = entry.PromptId, Status = "applied", NewVersion = 1 };
    }

    private async Task<SyncPushResponse> PushUpdateAsync(SpaceMembership membership, string? deviceId, Prompt prompt, PushPromptEntry entry, CancellationToken cancellationToken)
    {
        if (prompt.Version != entry.BaseVersion)
            return await RecordConflictAsync(prompt, entry, cancellationToken);

        var (categoryId, tagIds) = await ResolveCategoryAndTagsAsync(membership.SpaceID, entry.Payload!, cancellationToken);

        prompt.Title = entry.Payload!.Title;
        prompt.Content = entry.Payload.Content;
        prompt.Description = entry.Payload.Description;
        prompt.CategoryID = categoryId;
        prompt.UpdatedDeviceId = deviceId;
        // Version tang boi trigger vault.fn_prompts_before_update (Task 1) — khong tu tang o day.

        await ReplaceTagsAsync(prompt.PromptID, tagIds, cancellationToken);
        await ReplaceVariablesAsync(prompt.PromptID, entry.Payload.Variables, cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Race hiem: version doi giua luc doc va luc save trong CHINH request nay (2 push
            // gan nhu dong thoi). Nap lai ban moi nhat va ghi conflict thay vi de loi bung ra.
            _db.ChangeTracker.Clear();
            var latest = await _db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == prompt.PromptID, cancellationToken);
            return await RecordConflictAsync(latest, entry, cancellationToken);
        }

        return new SyncPushResponse { PromptId = entry.PromptId, Status = "applied", NewVersion = prompt.Version };
    }

    private async Task<SyncPushResponse> PushDeleteAsync(SpaceMembership membership, PushPromptEntry entry, CancellationToken cancellationToken)
    {
        var prompt = await _db.Prompts.FirstOrDefaultAsync(p => p.PromptID == entry.PromptId && p.SpaceID == membership.SpaceID, cancellationToken);
        if (prompt is null)
            // Da bi xoa/chua tung ton tai — coi nhu thanh cong, khop y dinh cua client (muon no bien mat).
            return new SyncPushResponse { PromptId = entry.PromptId, Status = "applied" };

        if (prompt.Version != entry.BaseVersion)
            return await RecordConflictAsync(prompt, entry, cancellationToken);

        prompt.IsDeleted = true;
        await _db.SaveChangesAsync(cancellationToken);

        return new SyncPushResponse { PromptId = entry.PromptId, Status = "applied", NewVersion = prompt.Version };
    }

    private async Task<SyncPushResponse> RecordConflictAsync(Prompt remote, PushPromptEntry entry, CancellationToken cancellationToken)
    {
        var localPayload = System.Text.Json.JsonSerializer.Serialize(entry.Payload);
        var remotePayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            remote.Title,
            remote.Content,
            remote.Description,
            remote.CategoryID
        });

        var conflict = new AioKin.Data.Entities.Sync.SyncConflict
        {
            EntityType = "prompt",
            EntityID = remote.PromptID,
            LocalPayloadJson = localPayload,
            RemotePayloadJson = remotePayload,
            LocalVersion = entry.BaseVersion,
            RemoteVersion = remote.Version
        };

        _db.SyncConflicts.Add(conflict);
        remote.HasConflict = true;
        await _db.SaveChangesAsync(cancellationToken);

        return new SyncPushResponse
        {
            PromptId = remote.PromptID,
            Status = "conflict",
            ConflictId = conflict.ConflictID,
            Remote = new AioKin.Models.ViewModel.Vault.PromptDetailResponse
            {
                PromptId = remote.PromptID,
                Title = remote.Title,
                Content = remote.Content,
                Description = remote.Description,
                Version = remote.Version,
                HasConflict = true
            }
        };
    }

    private async Task<(Guid? CategoryId, List<Guid> TagIds)> ResolveCategoryAndTagsAsync(Guid spaceId, AioKin.Models.InputModel.Vault.PromptPayload payload, CancellationToken cancellationToken)
    {
        Guid? categoryId = null;
        if (payload.CategoryId is { } wantedCategoryId)
        {
            var exists = await _db.Categories.AnyAsync(c => c.CategoryID == wantedCategoryId && c.SpaceID == spaceId, cancellationToken);
            if (!exists)
            {
                _db.Categories.Add(new Category { CategoryID = wantedCategoryId, SpaceID = spaceId, Name = payload.CategoryName ?? "Chua dat ten" });
            }
            categoryId = wantedCategoryId;
        }

        var tagIds = new List<Guid>();
        foreach (var tagRef in payload.Tags)
        {
            var exists = await _db.Tags.AnyAsync(t => t.TagID == tagRef.TagId && t.SpaceID == spaceId, cancellationToken);
            if (!exists)
                _db.Tags.Add(new Tag { TagID = tagRef.TagId, SpaceID = spaceId, Name = tagRef.Name });

            tagIds.Add(tagRef.TagId);
        }

        return (categoryId, tagIds);
    }

    private async Task ReplaceTagsAsync(Guid promptId, List<Guid> tagIds, CancellationToken cancellationToken)
    {
        var existing = await _db.PromptTags.Where(pt => pt.PromptID == promptId).ToListAsync(cancellationToken);
        _db.PromptTags.RemoveRange(existing);
        _db.PromptTags.AddRange(tagIds.Select(id => new PromptTag { PromptID = promptId, TagID = id }));
    }

    private async Task ReplaceVariablesAsync(Guid promptId, List<AioKin.Models.InputModel.Vault.PromptVariablePayload> variables, CancellationToken cancellationToken)
    {
        var existing = await _db.PromptVariables.Where(v => v.PromptID == promptId).ToListAsync(cancellationToken);
        _db.PromptVariables.RemoveRange(existing);
        _db.PromptVariables.AddRange(variables.Select(v => new PromptVariable
        {
            VariableID = v.VariableId, PromptID = promptId, VarKey = v.VarKey, Label = v.Label, DefaultValue = v.DefaultValue, VarType = v.VarType
        }));
    }
}
```

- [ ] **Step 6: `SyncController.cs`**

```csharp
using AioKin.Common;
using AioKin.Models.InputModel.Vault;
using AioKin.Services.Vault;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Vault;

[ApiController]
[Route("sync")]
[Produces("application/json")]
[Authorize(Roles = Roles.CUSTOMER)]
public class SyncController : ControllerBase
{
    private readonly ISyncService _syncService;

    public SyncController(ISyncService syncService)
    {
        _syncService = syncService;
    }

    [HttpPost("push")]
    public async Task<IActionResult> Push([FromBody] SyncPushRequest request, CancellationToken cancellationToken)
        => this.ToActionResult(await _syncService.PushAsync(request, cancellationToken));
}
```

- [ ] **Step 7: Register in `Program.cs`, run tests, commit**

```csharp
builder.Services.AddScoped<ISyncService, SyncService>();
```

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~SyncPushTests` → PASS.

```bash
git add -A
git commit -m "feat(sync): add POST /sync/push with version-mismatch conflict detection"
```

---

### Task 3: `IBlobStorageService` + `GET /sync/pull`

**Files:**
- Create: `AioKin/Services/Common/Storage/IBlobStorageService.cs`, `SupabaseStorageService.cs`
- Modify: `AioKin/Services/Vault/ISyncService.cs`, `SyncService.cs` (add pull)
- Modify: `AioKin/Controllers/Vault/SyncController.cs`
- Create: `AioKin.Tests/Vault/SyncPullTests.cs`

**Interfaces:**
- Produces: `IBlobStorageService.UploadAsync(string path, string content)`, `DownloadAsync(string path)` — gzip in both directions, same shape the deferred tiered-storage spec will reuse for prompt content later.
- Produces: `ISyncService.PullAsync(Guid spaceUuid, long? since, CancellationToken)`.

- [ ] **Step 1: `IBlobStorageService.cs` / `SupabaseStorageService.cs`**

```csharp
namespace AioKin.Services.Common.Storage;

/// <summary>
/// Upload/download gzip text len Supabase Storage. Dung truoc tien cho snapshot sync_catchup
/// (Task nay) — tinh nang externalize noi dung prompt > 8KB (spec, phan hoan lai) se dung
/// lai chinh service nay, khong tao ban thu hai.
/// </summary>
public interface IBlobStorageService
{
    Task<string> UploadAsync(string path, string content, CancellationToken cancellationToken = default);
    Task<string> DownloadAsync(string path, CancellationToken cancellationToken = default);
}
```

```csharp
using System.IO.Compression;
using System.Text;

namespace AioKin.Services.Common.Storage;

public class SupabaseStorageService : IBlobStorageService
{
    private readonly HttpClient _http;
    private const string Bucket = "promptvault";

    public SupabaseStorageService(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> UploadAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var compressed = Compress(content);
        var response = await _http.PostAsync($"/storage/v1/object/{Bucket}/{path}", new ByteArrayContent(compressed), cancellationToken);
        response.EnsureSuccessStatusCode();
        return path;
    }

    public async Task<string> DownloadAsync(string path, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"/storage/v1/object/{Bucket}/{path}", cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return Decompress(bytes);
    }

    private static byte[] Compress(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        using (var writer = new StreamWriter(gzip, Encoding.UTF8))
            writer.Write(text);
        return output.ToArray();
    }

    private static string Decompress(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
```

Register in `Program.cs`, next to `IEmailService`'s conditional registration (Supabase Storage credentials come from the same connection info as the Postgres connection — reuse the `Storage:BaseUrl` / `Storage:ServiceKey` config keys, following the same "skip gracefully if unconfigured" pattern `BrevoEmailService`/`LoggingEmailService` already use):

```csharp
var storageBaseUrl = config["Storage:BaseUrl"];
if (!string.IsNullOrWhiteSpace(storageBaseUrl))
{
    builder.Services.AddHttpClient<IBlobStorageService, SupabaseStorageService>(http =>
    {
        http.BaseAddress = new Uri(storageBaseUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("apikey", config["Storage:ServiceKey"]);
        http.Timeout = TimeSpan.FromSeconds(30);
    });
}
```

(the pull-fallback path in this task checks whether `IBlobStorageService` was registered and, if not, falls back to always serving incremental — see Step 4's `_blobStorage is null` branch — so a dev machine without Storage configured doesn't crash, matching the rest of the app's "missing optional dependency degrades, doesn't 500" convention.)

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using AioKin.Models.ViewModel.Vault;
using AioKin.Tests.Infrastructure;
using Xunit;

namespace AioKin.Tests.Vault;

[Collection(ApiCollection.Name)]
public class SyncPullTests
{
    private readonly ApiFixture _fixture;

    public SyncPullTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Pull_tu_dau_tra_ve_incremental_rong_khi_chua_co_thay_doi()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var mine = await user.Client.GetFromJsonAsync<OperationResultOf<System.Collections.Generic.List<SpaceResponse>>>("/spaces/me");
        var spaceUuid = mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;

        var response = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();
        Assert.False(body!.Data!.IsSnapshot);
        Assert.Empty(body.Data.Changes);
    }

    [Fact]
    public async Task Pull_sau_khi_push_thay_dung_thay_doi_do()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var mine = await user.Client.GetFromJsonAsync<OperationResultOf<System.Collections.Generic.List<SpaceResponse>>>("/spaces/me");
        var spaceUuid = mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;
        var promptId = Guid.NewGuid();

        await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            deviceId = "device-a",
            entities = new[] { new { promptId, operation = "insert", baseVersion = 0,
                payload = new { title = "V1", content = "noi dung", tags = Array.Empty<object>(), variables = Array.Empty<object>() } } }
        });

        var response = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since=0");
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();

        Assert.False(body!.Data!.IsSnapshot);
        Assert.Single(body.Data.Changes);
        Assert.Equal(promptId, body.Data.Changes[0].EntityId);
    }
}
```

- [ ] **Step 3: `SyncPullResponse.cs`**

```csharp
namespace AioKin.Models.ViewModel.Vault;

public class SyncPullResponse
{
    public bool IsSnapshot { get; set; }

    /// <summary>Chi co gia tri khi IsSnapshot = true.</summary>
    public string? SnapshotUrl { get; set; }

    public List<SyncChangeItem> Changes { get; set; } = [];

    /// <summary>Cursor de goi lai lan pull ke tiep.</summary>
    public long ResumeCursor { get; set; }
}

public class SyncChangeItem
{
    public long SyncLogId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public int Version { get; set; }
}
```

- [ ] **Step 4: Add `PullAsync` to `ISyncService`/`SyncService`**

```csharp
    Task<OperationResult> PullAsync(Guid spaceUuid, long since, CancellationToken cancellationToken = default);
```

```csharp
    private const int RowCountThreshold = 500;

    public async Task<OperationResult> PullAsync(Guid spaceUuid, long since, CancellationToken cancellationToken = default)
    {
        var membership = await _spaceContext.ResolveAsync(spaceUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("Forbidden", "Ban khong thuoc space nay.");

        // Retention check (bat buoc, dung/sai): cursor cu hon dong sync_log cu nhat con lai
        // nghia la mot phan lich su co the da bi cron xoa — khong the tra incremental an toan.
        var oldestLogId = await _db.SyncLog
            .Where(s => s.SpaceID == membership.SpaceID)
            .OrderBy(s => s.SyncLogID)
            .Select(s => (long?)s.SyncLogID)
            .FirstOrDefaultAsync(cancellationToken);

        var retentionExceeded = oldestLogId is not null && since < oldestLogId.Value - 1;

        // Volume check (toi uu, tuy chinh): so dong se phai tra vuot nguong thi snapshot re hon.
        var pendingCount = await _db.SyncLog.CountAsync(s => s.SpaceID == membership.SpaceID && s.SyncLogID > since, cancellationToken);
        var volumeExceeded = pendingCount > RowCountThreshold;

        if ((retentionExceeded || volumeExceeded) && _blobStorage is not null)
            return await BuildSnapshotFallbackAsync(membership, cancellationToken);

        var changes = await _db.SyncLog
            .AsNoTracking()
            .Where(s => s.SpaceID == membership.SpaceID && s.SyncLogID > since)
            .OrderBy(s => s.SyncLogID)
            .Select(s => new SyncChangeItem
            {
                SyncLogId = s.SyncLogID,
                EntityType = s.EntityType,
                EntityId = s.EntityID,
                Operation = s.Operation,
                PayloadJson = s.PayloadJson,
                Version = s.Version
            })
            .ToListAsync(cancellationToken);

        return OperationResult.Ok(data: new SyncPullResponse
        {
            IsSnapshot = false,
            Changes = changes,
            ResumeCursor = changes.Count > 0 ? changes[^1].SyncLogId : since
        });
    }

    private async Task<OperationResult> BuildSnapshotFallbackAsync(SpaceMembership membership, CancellationToken cancellationToken)
    {
        var prompts = await _db.Prompts
            .AsNoTracking()
            .Include(p => p.Variables)
            .Include(p => p.PromptTags).ThenInclude(pt => pt.Tag)
            .Where(p => p.SpaceID == membership.SpaceID && !p.IsDeleted)
            .ToListAsync(cancellationToken);

        var latestLogId = await _db.SyncLog
            .Where(s => s.SpaceID == membership.SpaceID)
            .OrderByDescending(s => s.SyncLogID)
            .Select(s => (long?)s.SyncLogID)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;

        var json = System.Text.Json.JsonSerializer.Serialize(prompts);
        var path = $"snapshots/{membership.SpaceUUID}/{DateTime.UtcNow:yyyyMMddHHmmss}.json.gz";
        var url = await _blobStorage!.UploadAsync(path, json, cancellationToken);

        _db.BackupSnapshots.Add(new AioKin.Data.Entities.Sync.BackupSnapshot
        {
            SpaceID = membership.SpaceID,
            TriggeredByUserID = membership.UserID,
            SnapshotType = "sync_catchup",
            StoragePath = url,
            FileSizeBytes = json.Length,
            PromptCount = prompts.Count
        });
        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Ok(data: new SyncPullResponse
        {
            IsSnapshot = true,
            SnapshotUrl = url,
            ResumeCursor = latestLogId
        });
    }
```

Add `private readonly IBlobStorageService? _blobStorage;` and a constructor parameter `IBlobStorageService? blobStorage = null` (optional injection — resolves to `null` when the app didn't register it, matching the Step 1 "degrades gracefully" note).

- [ ] **Step 5: Add the endpoint**

```csharp
    [HttpGet("pull")]
    public async Task<IActionResult> Pull([FromQuery] Guid spaceUuid, [FromQuery] long since, CancellationToken cancellationToken)
        => this.ToActionResult(await _syncService.PullAsync(spaceUuid, since, cancellationToken));
```

- [ ] **Step 6: Run tests, full suite, commit**

Run: `dotnet test` → PASS.

```bash
git add -A
git commit -m "feat(sync): add GET /sync/pull with retention/volume snapshot fallback"
```

---

### Task 4: `POST /sync/conflicts/{id}/resolve`

**Files:**
- Create: `AioKin/Models/InputModel/Vault/ResolveConflictRequest.cs`
- Modify: `AioKin/Services/Vault/ISyncService.cs`, `SyncService.cs`
- Modify: `AioKin/Controllers/Vault/SyncController.cs`
- Create: `AioKin.Tests/Vault/SyncConflictResolveTests.cs`

- [ ] **Step 1: `ResolveConflictRequest.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Vault;

public class ResolveConflictRequest
{
    /// <summary>"keep_local" | "keep_remote" | "merged".</summary>
    [Required]
    public required string Resolution { get; set; }

    /// <summary>Bat buoc khi Resolution = "merged".</summary>
    public PromptPayload? MergedPayload { get; set; }
}
```

- [ ] **Step 2: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using AioKin.Data.Entities.Vault;
using AioKin.Models.ViewModel.Vault;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Vault;

[Collection(ApiCollection.Name)]
public class SyncConflictResolveTests
{
    private readonly ApiFixture _fixture;

    public SyncConflictResolveTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Resolve_keep_local_ghi_de_ban_tren_server_bang_ban_local()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var mine = await user.Client.GetFromJsonAsync<OperationResultOf<System.Collections.Generic.List<SpaceResponse>>>("/spaces/me");
        var spaceUuid = mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;
        var promptId = Guid.NewGuid();

        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, deviceId = "a", entities = new[] { new { promptId, operation = "insert", baseVersion = 0,
            payload = new { title = "V1", content = "goc", tags = Array.Empty<object>(), variables = Array.Empty<object>() } } } });
        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, deviceId = "b", entities = new[] { new { promptId, operation = "update", baseVersion = 1,
            payload = new { title = "Tu B", content = "noi dung B", tags = Array.Empty<object>(), variables = Array.Empty<object>() } } } });

        var conflictResponse = await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, deviceId = "a", entities = new[] { new { promptId, operation = "update", baseVersion = 1,
            payload = new { title = "Tu A", content = "noi dung A", tags = Array.Empty<object>(), variables = Array.Empty<object>() } } } });
        var conflictBody = await conflictResponse.Content.ReadFromJsonAsync<OperationResultOf<System.Collections.Generic.List<SyncPushResponse>>>();
        var conflictId = conflictBody!.Data![0].ConflictId!.Value;

        var resolveResponse = await user.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new
        {
            resolution = "keep_local",
            mergedPayload = new { title = "Tu A", content = "noi dung A", tags = Array.Empty<object>(), variables = Array.Empty<object>() }
        });

        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.Equal("Tu A", live.Title);
        Assert.False(live.HasConflict);

        var conflict = await db.SyncConflicts.AsNoTracking().FirstAsync(c => c.ConflictID == conflictId);
        Assert.True(conflict.Resolved);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test AioKin.Tests --filter FullyQualifiedName~SyncConflictResolveTests` → FAIL, 404.

- [ ] **Step 4: Add `ResolveConflictAsync`**

```csharp
    Task<OperationResult> ResolveConflictAsync(Guid conflictId, ResolveConflictRequest request, CancellationToken cancellationToken = default);
```

```csharp
    public async Task<OperationResult> ResolveConflictAsync(Guid conflictId, ResolveConflictRequest request, CancellationToken cancellationToken = default)
    {
        var conflict = await _db.SyncConflicts.FirstOrDefaultAsync(c => c.ConflictID == conflictId && !c.Resolved, cancellationToken);
        if (conflict is null)
            return OperationResult.Fail("NotFound", "Khong tim thay xung dot can xu ly.");

        var prompt = await _db.Prompts.FirstOrDefaultAsync(p => p.PromptID == conflict.EntityID, cancellationToken);
        if (prompt is null)
            return OperationResult.Fail("NotFound", "Prompt khong con ton tai.");

        // "keep_remote" khong sua noi dung — chi don co conflict va ghi nhan da xu ly.
        if (request.Resolution == "keep_local" || request.Resolution == "merged")
        {
            var payload = request.Resolution == "merged"
                ? request.MergedPayload
                : System.Text.Json.JsonSerializer.Deserialize<AioKin.Models.InputModel.Vault.PromptPayload>(conflict.LocalPayloadJson);

            if (payload is null)
                return OperationResult.Fail("ValidationError", "Thieu mergedPayload cho resolution 'merged'.");

            prompt.Title = payload.Title;
            prompt.Content = payload.Content;
            prompt.Description = payload.Description;
        }

        prompt.HasConflict = false;
        conflict.Resolved = true;
        conflict.ResolutionStrategy = request.Resolution;
        conflict.ResolvedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return OperationResult.Ok("Da xu ly xung dot.");
    }
```

- [ ] **Step 5: Add the endpoint**

```csharp
    [HttpPost("conflicts/{conflictId:guid}/resolve")]
    public async Task<IActionResult> ResolveConflict(Guid conflictId, [FromBody] ResolveConflictRequest request, CancellationToken cancellationToken)
        => this.ToActionResult(await _syncService.ResolveConflictAsync(conflictId, request, cancellationToken));
```

- [ ] **Step 6: Run tests, full suite, `detect_changes()`, commit**

Run: `dotnet test` → PASS. Run `detect_changes({scope: "compare", base_ref: "feat/promptvault-space-and-prompt-domain"})`.

```bash
git add -A
git commit -m "feat(sync): add POST /sync/conflicts/{id}/resolve"
```

---

## After this plan

The backend side of the PromptVault merge is complete: Space/Family unification, Prompt domain, and a Git-style two-way sync engine with a snapshot fallback for far-behind or high-volume pulls. What's left, per the spec's section 2 (deferred) and section 7 (client architecture): the actual Kotlin/RN client sync engines (outbox tables, background workers — designed conceptually in the spec, implemented in their own repos), `prompt_versions`/edit history, and AI enrichment.
