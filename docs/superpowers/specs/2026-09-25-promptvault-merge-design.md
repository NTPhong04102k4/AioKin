# PromptVault Merge — Design Spec

**Status:** Draft, awaiting review before writing-plans.
**Supersedes design intent (not code) in:** `db/init-postgres.sql`, `docs/PromptVault_Storage_Optimization_And_AI_Enrichment.md`.
**Related, already-designed but not yet implemented:** `docs/auth-opaque-tokens-biometric.md` and its 3 plans in `docs/superpowers/plans/2026-09-25-*.md`.

## 1. Goal

Merge PromptVault's Space/Prompt domain into AioKin. One ASP.NET Core backend serves **both** the existing native Kotlin Android app and a **new** React Native + Expo app, with no client-specific endpoints. Family (M0, shipped) is generalized into a broader `Space` concept without renaming or migrating its existing tables. Prompts sync two-way between server and both clients, offline-first, with Git-style conflict handling (no automatic Last-Write-Wins).

## 2. Scope for this spec

**In scope:** Space model (personal/family/team), Prompt domain (prompts, categories, tags, variables), two-way sync + conflict resolution, device identity unification, client sync architecture (Kotlin + RN, conceptual).

**Explicitly deferred (separate, later spec):**
- `prompt_versions` / full edit history — the draft SQL already has the table; this iteration does not wire it up. Revisit when someone actually asks to roll back an edit.
- AI enrichment (`raw_input`, `enrichment_cache`, LLM calls) — `docs/PromptVault_Storage_Optimization_And_AI_Enrichment.md` section 2.
- Tiered storage (>8KB → object storage) — **already applied to `db/init-postgres.sql` section 3.1** ahead of schedule (user requested it directly mid-session). It's included in this spec's schema as a given, but the *service-layer* implementation (`IBlobStorageService`, the retrofit background job) is still deferred to the follow-up spec, same as originally planned.
- Creating the actual React Native repo, and any Kotlin-app-specific code — those happen in their own repos, outside this session.

## 3. Corrections required to the draft SQL before it becomes a real EF migration

`db/init-postgres.sql` was hand-written as a standalone design sketch, not derived from AioKin's actual schema. Three problems in it are not stylistic — they would break at runtime if implemented as written.

### 3.1. `security.users` collision

The draft's `security.users` (email, username, password_hash, full_name...) is a **different, simpler table** than AioKin's real `security.users` (from `AioKin/Data/Entities/Security/User.cs`, which already has `UserCode`, `UserUUID`, OAuth/OTP fields, lockout tracking, etc.). Both can't exist under the same schema-qualified name.

**Fix:** the merged design uses AioKin's existing `User` entity as-is. Every `vault.*`/`sync.*` FK that reads `security.users (user_id)` in the draft instead targets AioKin's real `security.users (user_id)` — same `uuid` type, so no type change needed, just a different (richer) table on the other end. `security.user_identities` (multi-login) and `security.user_profiles` (theme persist) are genuinely new AioKin doesn't have yet — these become new EF entities, additive, no collision.

### 3.2. RLS policies don't apply to this architecture

Section 6 of the draft (`rls_prompts_select`, etc.) uses `auth.uid()` — a Supabase Auth function that resolves the *current Supabase session's* user. AioKin explicitly does not use Supabase Auth (`docs/database.md`: "tự quản lý user/profile độc lập, không phụ thuộc Supabase Auth") and the backend talks to Postgres through one pooled connection, not a per-user Supabase session. `auth.uid()` would be `NULL` for every query the API ever runs.

**Fix:** drop RLS entirely from the merged design. Authorization stays exactly where the rest of AioKin already puts it — in the API layer (`[Authorize]`, `IPermissionService`/CASL rules, and a new `ISpaceContext` — see section 5). This isn't a downgrade: RLS in the draft was never going to do anything in this architecture; removing it removes false confidence, not real protection.

### 3.3. Version-bump trigger is too broad

`vault.fn_prompts_before_update` increments `version` on **any** `UPDATE`, including ones that only flip `has_conflict` or `is_favorite`. Since sync conflict detection is `base_version == current version`, an unrelated favorite-toggle bumping the version would make a concurrent, genuinely-unconflicting content edit look like a conflict.

**Fix:** scope the trigger to only fire when content-affecting columns actually change:

```sql
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
```

`has_conflict`, `is_favorite`, `is_archived`, `usage_count` updates go through without touching `version` or `sync_log`.

## 4. Space model — Family generalized

Family (M0, shipped) is not renamed or migrated. `Space` is a new, thin layer above it.

```
vault.spaces
    space_id        uuid PK
    space_type      'personal' | 'family' | 'team'
    name            varchar(120)
    owner_user_id   uuid -> security.users(user_id)
    family_id       uuid NULL, UNIQUE, -> family.families(family_id)   -- only when space_type='family'
    is_active       boolean
    created_date    timestamptz

vault.space_members            -- ONLY for space_type='team'
    space_id     uuid -> vault.spaces
    user_id      uuid -> security.users
    member_role  'owner' | 'admin' | 'member'      -- trimmed from the draft's 5-role set
    joined_date  timestamptz
```

Rules:
- **Personal:** exactly one `space_type='personal'` row per user, auto-created lazily on first touch (registration, or first Prompt-domain call if missing). Owner is always the sole member — no `space_members` row needed.
- **Family:** membership and roles are answered by the *existing* `family_members` table, reached through `family_id`. `vault.space_members` has no rows for family-type spaces — don't duplicate what `family_members` already owns.
- **Team:** the only genuinely new membership model. Household roles (Owner/Adult/Child from M0) don't fit a work group, hence the separate, trimmed Owner/Admin/Member set.

**`ISpaceContext`** (new service, composes over the existing `IFamilyContext` — does not modify it) answers one question: "is this caller a member of this space, and with what role?" — routing to `family_members` or `space_members` depending on `space_type`, and to "is caller == owner_user_id" for personal. Every Prompt-domain controller calls this instead of querying membership itself, mirroring how `IFamilyContext` already works for M0.

## 5. Prompt domain — what ships now

`vault.categories`, `vault.tags`, `vault.prompt_tags`, `vault.prompts` (including the already-applied tiered-storage columns), `vault.prompt_variables` — as drafted in `db/init-postgres.sql`, corrected per section 3. `vault.prompt_versions` exists in the draft SQL but is **not** part of this iteration's EF migration (deferred per section 2).

CASL: `Prompt`, `Category`, `Tag` each get a `SubjectType` constant and a seeded rule, following the exact pattern `Family`/`FamilyMember` already established in `DbSeeder.SeedRolePermissionsAsync` — this is a hard repo convention (`ke-hoach-mo-rong.md` section 1.1), not optional.

## 6. Two-way sync — no automatic conflict resolution

### 6.1. Client generates the permanent ID

`prompt_id` (and `category_id`, `tag_id`) is generated **client-side** at creation time and used as-is forever. This eliminates the local-id-to-server-id remapping problem entirely — the draft's original `local_id` column is removed (already applied).

### 6.2. Push (client → server)

```
POST /sync/push  { entities: [{ entityType, entityId, operation, payload, baseVersion, editedAt }] }
```

For each entity, the server issues a conditional update:

```sql
UPDATE vault.prompts SET title = @title, content = @content, ...
WHERE prompt_id = @id AND version = @baseVersion;
```

- **1 row affected** → clean write (no race — Postgres handles the atomicity, not a read-then-compare-then-write in application code). The existing triggers bump `version` and append to `sync_log` automatically.
- **0 rows affected** → conflict. The server does **not** retry or fall back to any heuristic. It reads the current live row, writes both payloads to `sync.sync_conflicts` (`local_payload` = what the client tried to push, `remote_payload` = current live state, both versions), sets `vault.prompts.has_conflict = true`, and tells the client this entity is now conflicted (with the current remote payload attached, so the client doesn't need a second round-trip to see what it conflicts with).

**No timestamp comparison, ever.** Every version mismatch — edit-vs-edit, edit-vs-delete, delete-vs-edit — is the same conflict path. This sidesteps clock-skew entirely (the concern raised earlier) because no clock is ever consulted for the decision.

### 6.3. Resolve

```
POST /sync/conflicts/{conflictId}/resolve  { resolution: "keep_local" | "keep_remote" | "merged", mergedPayload? }
```

Writes the chosen content as a normal update against the *current* server version (a clean fast-forward, since the conflict record already captured what "current" was), sets `resolved = true`, `resolution_strategy`, `resolved_at`, clears `has_conflict`. Both clients show this the same way: a side-by-side "your version / server version" view (same layout pattern as the AI-enrichment preview screen already sketched in the storage-optimization doc — reuse it, don't design a new layout).

### 6.4. Pull (server → client) — hybrid, table for the common case, file for the rare one

```
GET /sync/pull?spaceId=X&since=<cursor>
```

1. **Retention check (mandatory, correctness):** if `since` is older than what `sync.sync_log` still retains (the existing `fn_cleanup_old_sync_log` cron already prunes at 30/180 days) — the incremental log for that range is gone. Serving an incremental pull here would silently omit changes. Must fall through to the snapshot path.
2. **Volume check (tunable, performance):** if the incremental result would exceed a row-count threshold (default 500 — chosen because it targets what's actually expensive, transfer volume, rather than "how long the device was offline," which is a proxy that misses a busy space with a short offline gap), also fall through to the snapshot path.
3. **Snapshot path:** reuse `sync.backup_snapshots` (already exists in the draft SQL for manual/scheduled backups) with a new `snapshot_type = 'sync_catchup'`. Server serves the most recent one (or builds one on demand if none is fresh enough), the client does a full local replace for that space, and resumes incremental pulls from `resume_cursor` = the `sync_log_id` current at snapshot build time.
4. **Otherwise:** stream `sync_log` rows after `since`, ordered by `created_at` — the common case, cheap, index-backed, unchanged from the original draft design.

This also fixes a latent bug in the original draft: a device offline longer than the retention window had no fallback and would have silently missed changes on incremental pull.

### 6.5. Device identity — one table, not three

Three places in this body of work independently need "which device is this": `AccessTokenSession`/`RefreshTokenPayload` (opaque tokens, deviceId/deviceName/platform), `security.device_credentials` (biometric, same three fields), and `sync.devices` (same three fields again). Once merged into one database, these should not carry three separate copies of device metadata.

**Decision:** `sync.devices` becomes the canonical device registry (`device_id`, `user_id`, `device_name`, `platform`, `app_version`, `last_synced_at`, `is_stale` — as already drafted). `security.device_credentials` (from the biometric plan) references `sync.devices(device_id)` instead of storing its own `device_name`/`platform`. The opaque-token `DeviceInfo` snapshotted into a Redis session at issue time stays as-is (it's an ephemeral cache value, not a persistent record — no normalization needed there), but registers/updates the row in `sync.devices` on first use of a new `deviceId`, so the canonical table is always populated from whichever feature saw the device first.

## 7. Client sync architecture (conceptual — implementation lives in each client's own repo)

Neither client has local persistence today (confirmed: `NativeKotlin`'s `Data` module is only `SessionManager` + Retrofit, no Room). Both build the same conceptual pieces:

| Piece | Kotlin (Android) | React Native + Expo |
|---|---|---|
| Local storage | Room | `expo-sqlite`, hand-written SQL (no extra ORM — matches the backend's own SQL-first style, avoids an added dependency for a greenfield app) |
| Local schema | Mirrors `vault.*` tables the device can see, plus a `sync_outbox` table for queued writes | Same shape |
| Outbox | Every local write (offline or online) inserts into `sync_outbox` first, then attempts immediate push if online | Same |
| Push trigger | WorkManager: on network reconnect + periodic (e.g. every 15 min) | Background task (`expo-task-manager`) on the same triggers |
| Pull trigger | On app foreground + after every successful push + periodic | Same |
| Conflict UI | Side-by-side compare screen, reusing the app's existing prompt-edit screen shell | Same layout, RN components |

This table is the extent of client design in this spec — the actual Room DAOs / RN data layer are implementation details for a plan written from inside those repos, not from here.

## 8. What changes in the actual EF migration vs. the draft SQL

The draft SQL stays as a **design reference** (already useful, already corrected per section 3) but is not what gets run. The real implementation path is AioKin's existing convention: C# entities under `AioKin/Data/Entities/Vault/` and `AioKin/Data/Entities/Sync/`, registered in `AioKinDbContext`, migrated with `dotnet ef migrations add AddPromptVault`. `db/init-postgres.sql` itself should eventually go back to being a *generated* artifact (`dotnet ef migrations script`), per its own documented convention in `docs/database.md` — once the real migration exists, this hand-written file's job is done.

## 9. Testing approach

Follow the pattern already established for the auth plans: xUnit + `ApiFixture` (real Postgres via `WebApplicationFactory`) for anything touching the database or HTTP pipeline; plain xUnit for pure logic (e.g., the conflict-detection decision itself, isolated from Postgres, the same way `BiometricSignatureTests` isolated ECDSA verification from the database in the biometric plan). Minimum coverage the plan must include: a clean push (fast-forward), a conflicting push (0 rows affected → conflict record created, `has_conflict` set), a resolve for each of the three strategies, a pull under the row-count threshold (incremental), a pull over it (snapshot fallback), and a pull with a `since` older than retention (snapshot fallback, proving the latent bug is fixed).

## 10. Open questions carried into the plan, not blocking this spec

- Exact row-count default (500) and retention window are constants, not architecture — fine to tune later without a design change.
- Whether `Category`/`Tag` need their own `ISpaceContext`-gated CASL rules distinct from `Prompt`'s, or inherit the same rule — a plan-level detail, decided when writing `DbSeeder` code, not here.
