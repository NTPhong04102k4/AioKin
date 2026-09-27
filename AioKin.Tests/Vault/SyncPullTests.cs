using System.Net;
using System.Net.Http.Json;
using AioKin.Data.Entities.Sync;
using AioKin.Data.Entities.Vault;
using AioKin.Models.ViewModel.Vault;
using AioKin.Services.Auth.Token;
using AioKin.Services.Vault;
using AioKin.Tests.Auth;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AioKin.Tests.Vault;

/// <summary>
/// GET /sync/pull — incremental hoac snapshot (retention/khoi luong vuot nguong). Xem
/// .superpowers/sdd/2026-09-25-promptvault-sync-engine/progress.md muc "Task 3" cho toan bo
/// ruling, va task-3-report.md ("Fix round 1") cho 5 finding sua trong lan nay ma cac test duoi
/// day bam theo.
///
/// CRITICAL #1: since=0 gio LUON tra ve SNAPSHOT (fix round 1, finding 1) — khong con incremental
/// nao cho since=0 nua. Moi test muon kiem tra hanh vi INCREMENTAL (echo suppression, hydrate,
/// dedup, safety window...) phai tu tao mot cursor THAT SU khac 0 truoc (xem
/// PushOnceAndGetCursorAsync/EstablishCursorThenCreateRetentionGapAsync).
///
/// CRITICAL #2: _safetyWindow gio la 10s theo mac dinh (fix round 1, finding 3b) — hau het test
/// goi AgeSyncLogAsync() de "lam gia" cac dong sync_log truoc khi pull, mo phong dieu kien thuc
/// te (du thoi gian troi qua) thay vi Task.Delay that (cham + kem tin cay hon).
/// </summary>
[Collection(ApiCollection.Name)]
public class SyncPullTests
{
    private readonly ApiFixture _fixture;

    public SyncPullTests(ApiFixture fixture) => _fixture = fixture;

    private static async Task<Guid> GetPersonalSpaceUuidAsync(TestUser user)
    {
        var mine = await user.Client.GetFromJsonAsync<OperationResultOf<List<SpaceResponse>>>("/spaces/me");
        return mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;
    }

    private static object InsertEntry(Guid promptId, string title, string content, object? tags = null, object? variables = null)
        => new { promptId, operation = "insert", baseVersion = 0, payload = new { title, content, tags, variables } };

    private static object UpdateEntry(Guid promptId, int baseVersion, string title, string content, object? tags = null, object? variables = null)
        => new { promptId, operation = "update", baseVersion, payload = new { title, content, tags, variables } };

    /// <summary>
    /// "Lam gia" moi dong sync_log cua mot space qua nguong _safetyWindow — mo phong thoi gian
    /// troi qua thay vi Task.Delay that trong test.
    /// </summary>
    private async Task AgeSyncLogAsync(Guid spaceUuid, int seconds = 15)
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sync.sync_log SET created_at = now() - make_interval(secs => {seconds}) WHERE space_id = {space.SpaceID}");
    }

    /// <summary>
    /// Push MOT thay doi "setup" (khong lien quan truc tiep den dieu dang test) roi pull ngay
    /// (since=0 — fix round 1, finding 1: LUON la snapshot) chi de lay ResumeCursor CO THAT lam
    /// diem xuat phat cho cac buoc INCREMENTAL tiep theo trong test. since=0 khong con dung duoc
    /// de quan sat hanh vi incremental (echo suppression/hydrate/dedup/safety-window) vi no luon
    /// di qua nhanh snapshot, bat ke trang thai that su.
    /// </summary>
    private async Task<long> PushOnceAndGetCursorAsync(TestUser user, Guid spaceUuid, string title = "SETUP", string content = "setup")
    {
        var promptId = Guid.NewGuid();
        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, title, content) } });
        await AgeSyncLogAsync(spaceUuid);

        var response = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since=0");
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();
        return body!.Data!.ResumeCursor;
    }

    /// <summary>
    /// Thiet lap mot cursor THAT SU roi tao mot KHOANG TRONG that su phia sau cursor do — mo
    /// phong dung dieu kien retentionExceeded (xem SyncService.PullAsync, nhanh since &gt; 0).
    /// </summary>
    private async Task<long> EstablishCursorThenCreateRetentionGapAsync(TestUser user, Guid spaceUuid)
    {
        var cursor = await PushOnceAndGetCursorAsync(user, spaceUuid, "A", "noi dung A");

        var promptB = Guid.NewGuid();
        var promptC = Guid.NewGuid();
        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptB, "B", "noi dung B") } });
        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptC, "C", "noi dung C") } });

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
        // Mo phong cron retention: chi giu lai dong MOI NHAT (C), xoa CA dong cursor tro toi (A)
        // LAN dong ke tiep (B) — tao mot khoang trong THAT SU giua cursor va dong con lai.
        var maxId = await db.SyncLog.Where(s => s.SpaceID == space.SpaceID).MaxAsync(s => s.SyncLogID);
        var toDelete = await db.SyncLog.Where(s => s.SpaceID == space.SpaceID && s.SyncLogID < maxId).ToListAsync();
        db.SyncLog.RemoveRange(toDelete);
        await db.SaveChangesAsync();

        // Fix round 1, finding 2: BuildSnapshotFallbackAsync gio cung ap dung _safetyWindow cho
        // ResumeCursor — C phai "du gia" thi cursor cua lan pull snapshot sau nay moi phan anh no.
        await AgeSyncLogAsync(spaceUuid);

        return cursor;
    }

    /// <summary>ISpaceContext that doc HttpContext (khong co trong mot scope test tran) — dung
    /// khi can goi thang SyncService.PullAsync ngoai mot HTTP request that.</summary>
    private sealed class FixedSpaceContext : ISpaceContext
    {
        private readonly SpaceMembership _membership;
        public FixedSpaceContext(SpaceMembership membership) => _membership = membership;
        public Task<SpaceMembership?> ResolveAsync(Guid spaceUuid, CancellationToken cancellationToken = default) => Task.FromResult<SpaceMembership?>(_membership);
        public Task InvalidateAsync(Guid spaceUuid, Guid userUuid, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Fix round 1, finding 1: since=0 LUON la snapshot, ke ca khi space chua co gi ca
    /// (Prompts rong) — khong con nhanh "incremental rong" cho since=0.</summary>
    [Fact]
    public async Task Pull_since_0_luon_tra_snapshot_ke_ca_khi_chua_co_gi()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);

        var response = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();
        Assert.True(body!.Data!.IsSnapshot);
        Assert.NotNull(body.Data.SnapshotJson);
        Assert.Contains(""""Prompts":[]"""", body.Data.SnapshotJson.Replace(" ", ""));
        Assert.Equal(0, body.Data.ResumeCursor);
    }

    /// <summary>
    /// Fix round 1, finding 1 (P13 violation): mot device MOI pull LAN DAU (since=0) SAU KHI
    /// lich su sync_log cua space da bi cron retention xoa bot phai van nhan duoc TOAN BO trang
    /// thai hien tai (snapshot), khong duoc nhan mot ket qua incremental thieu voi ve ngoai "da
    /// dong bo day du".
    /// </summary>
    [Fact]
    public async Task Pull_since_0_sau_khi_retention_da_xoa_bot_van_tra_snapshot_day_du()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);

        // Tao lich su roi "cron retention" xoa het sync_log (khong dong cha den dong con nao con
        // lai) — nhung cac prompt VAN CON SONG trong promptvault.prompts.
        await EstablishCursorThenCreateRetentionGapAsync(user, spaceUuid);
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
            db.SyncLog.RemoveRange(db.SyncLog.Where(s => s.SpaceID == space.SpaceID));
            await db.SaveChangesAsync();
        }

        var response = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since=0");
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();

        Assert.True(body!.Data!.IsSnapshot);
        Assert.NotNull(body.Data.SnapshotJson);
        Assert.Contains("noi dung A", body.Data.SnapshotJson);
        Assert.Contains("noi dung B", body.Data.SnapshotJson);
        Assert.Contains("noi dung C", body.Data.SnapshotJson);
    }

    /// <summary>
    /// Carry-forward Task 2/3: chinh (user, device) vua tao ra mot thay doi khong can duoc bao
    /// lai thay doi cua chinh no tren nhanh INCREMENTAL (since &gt; 0 — since=0 gio luon la
    /// snapshot, xem PushOnceAndGetCursorAsync). Can mot device KHONG NULL (fix round 1, finding
    /// 5: callerDeviceId=null khong bao gio suppress).
    /// </summary>
    [Fact]
    public async Task Pull_cung_session_sau_khi_push_bi_an_chinh_thay_doi_cua_no()
    {
        var user = await TestUser.CreateAsync(_fixture, DeviceInfo.Resolve("dev-a", null, null));
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var cursor = await PushOnceAndGetCursorAsync(user, spaceUuid);

        var promptId = Guid.NewGuid();
        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "V1", "noi dung") } });
        await AgeSyncLogAsync(spaceUuid);

        var response = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since={cursor}");
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();

        Assert.False(body!.Data!.IsSnapshot);
        Assert.Empty(body.Data.Changes);
        // Cursor van phai TIEN qua dong bi echo-suppress, khong thi lan pull sau se cu phai xin
        // lai (roi lai an di) chinh thay doi nay moi lan.
        Assert.True(body.Data.ResumeCursor > cursor);
    }

    /// <summary>
    /// Fix round 1, finding 5 (cheap correctness fix): callerDeviceId=null nghia la "khong biet
    /// thiet bi nao", KHONG phai "mot thiet bi cu the ten null" — hai phien null-device CUA CUNG
    /// MOT USER (vd web chua gui DeviceInfo) khong duoc suppress lan nhau. Truoc fix, null==null
    /// khien thay doi hop le nay bi an sai.
    /// </summary>
    [Fact]
    public async Task Echo_suppression_khong_ap_dung_khi_khong_biet_thiet_bi()
    {
        var user = await TestUser.CreateAsync(_fixture); // DeviceInfo.Unknown -> deviceId null
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var cursor = await PushOnceAndGetCursorAsync(user, spaceUuid);

        var promptId = Guid.NewGuid();
        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "V1", "noi dung") } });
        await AgeSyncLogAsync(spaceUuid);

        var response = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since={cursor}");
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();

        Assert.Single(body!.Data!.Changes);
        Assert.Equal(promptId, body.Data.Changes[0].EntityId);
    }

    /// <summary>Mot thiet bi KHAC cua CHINH user do (khac (user, device) voi thiet bi vua push)
    /// phai THAY duoc thay doi — chi (user, device) TRUNG moi bi an.</summary>
    [Fact]
    public async Task Pull_tu_thiet_bi_khac_cua_cung_user_thay_duoc_thay_doi()
    {
        var owner = await TestUser.CreateAsync(_fixture, DeviceInfo.Resolve("dev-a", null, null));
        var spaceUuid = await GetPersonalSpaceUuidAsync(owner);
        var cursor = await PushOnceAndGetCursorAsync(owner, spaceUuid);

        var promptId = Guid.NewGuid();
        await owner.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "V1", "noi dung") } });
        await AgeSyncLogAsync(spaceUuid);

        using var deviceBClient = await owner.CreateAdditionalDeviceClientAsync(_fixture, DeviceInfo.Resolve("dev-b", null, null));
        var response = await deviceBClient.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since={cursor}");
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();

        Assert.Single(body!.Data!.Changes);
        Assert.Equal(promptId, body.Data.Changes[0].EntityId);
        Assert.Equal("insert", body.Data.Changes[0].Operation);
        Assert.Equal("V1", body.Data.Changes[0].Prompt!.Title);
        Assert.False(body.Data.Changes[0].Prompt!.IsDeleted);
    }

    /// <summary>
    /// Carry-forward Task 2 (SECURITY): echo suppression MOT MINH tren origin_device_id la
    /// spoofable — 2 THANH VIEN KHAC NHAU cua cung mot space chia se co the tu chon trung
    /// DeviceInfo.DeviceId. Mo phong bang cach ghi thang mot dong sync_log co CUNG device_id
    /// nhung OriginUserId la MOT NGUOI KHAC — pull van phai thay duoc dong nay (khong bi an sai).
    /// </summary>
    [Fact]
    public async Task Echo_suppression_dung_tren_cap_user_device_khong_chi_device()
    {
        var user = await TestUser.CreateAsync(_fixture, DeviceInfo.Resolve("shared-device", null, null));
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var cursor = await PushOnceAndGetCursorAsync(user, spaceUuid);

        var promptFromOther = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
            db.SyncLog.Add(new SyncLogEntry
            {
                SpaceID = space.SpaceID,
                EntityType = "prompt",
                EntityID = promptFromOther,
                Operation = "insert",
                PayloadJson = """{"title":"Tu nguoi khac","content":"C","description":null,"category_id":null,"is_deleted":false}""",
                OriginDeviceId = "shared-device", // TRUNG voi device cua user hien tai
                OriginUserId = otherUserId,       // NHUNG la MOT NGUOI KHAC
                Version = 1
            });
            await db.SaveChangesAsync();
        }
        await AgeSyncLogAsync(spaceUuid);

        var response = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since={cursor}");
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();

        // Neu suppress chi dua tren device (loi cu bi ruling nay chi ra), dong nay se bi an mat
        // dung ly do "trung device_id" — day chinh la lo hong gia mao.
        Assert.Single(body!.Data!.Changes);
        Assert.Equal(promptFromOther, body.Data.Changes[0].EntityId);
        Assert.Equal("Tu nguoi khac", body.Data.Changes[0].Prompt!.Title);
    }

    /// <summary>Carry-forward Task 2 + follow-up meta_sig: mot thay doi CHI tag phai duoc pull
    /// hydrate Tags/Title/Content tu BANG SONG (tags/variables luon hydrate tu bang song, KHONG
    /// BAO GIO tu PayloadJson — trigger khong biet gi ve prompt_tags/prompt_variables). Follow-up
    /// (tag/variable-only versioning gap): truoc day dong nay duoc CHINH SyncService tu ghi thu
    /// cong voi "kind":"tags_variables" (P12 workaround, TagsVariablesOnly=true) VI trigger DB bo
    /// qua hoan toan thay doi CHI-tag (khong bump version). Gio Prompt.MetaSig doi buoc trigger
    /// tu bump version + tu ghi sync_log (payload la to_jsonb(NEW) — CA HANG that, khong co
    /// truong "kind") — TagsVariablesOnly gio la false cho dong nay (khong con workaround thu
    /// cong nua), nhung Title/Content van DUNG (doc thang tu PayloadJson cua chinh dong, vi day
    /// la snapshot HANG THAT tai thoi diem do, khong phai "live" fallback nhu truoc).</summary>
    [Fact]
    public async Task Pull_hydrate_tags_tu_bang_song_cho_thay_doi_chi_tag()
    {
        var user = await TestUser.CreateAsync(_fixture, DeviceInfo.Resolve("dev-a", null, null));
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var cursor = await PushOnceAndGetCursorAsync(user, spaceUuid);

        var promptId = Guid.NewGuid();
        var tagId = Guid.NewGuid();
        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "T1", "C1") } });
        await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { UpdateEntry(promptId, 1, "T1", "C1", tags: new[] { new { tagId, name = "moi" } }) }
        });
        await AgeSyncLogAsync(spaceUuid);

        using var deviceBClient = await user.CreateAdditionalDeviceClientAsync(_fixture, DeviceInfo.Resolve("dev-b", null, null));
        var response = await deviceBClient.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since={cursor}");
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();

        Assert.Equal(2, body!.Data!.Changes.Count);
        var tagOnlyChange = body.Data.Changes[^1];
        // Follow-up: trigger DB gio tu bump version/ghi sync_log cho thay doi CHI tag (xem
        // SyncMetaSigTests) — dong nay khong con mang "kind":"tags_variables" (do khong con la
        // workaround thu cong), nen TagsVariablesOnly la false. Van la mot cai tien dung (P22 gio
        // "thay" duoc thay doi CHI-tag nay qua Version), chi la mat mot HINT rieng cho client (ve
        // ban chat khong can thiet: Title/Content trong payload van dung du co hint hay khong).
        Assert.False(tagOnlyChange.TagsVariablesOnly);
        Assert.Contains("moi", tagOnlyChange.Prompt!.Tags);
        Assert.Equal("T1", tagOnlyChange.Prompt.Title);
        Assert.Equal("C1", tagOnlyChange.Prompt.Content);
    }

    /// <summary>
    /// Carry-forward Task 1 (dedup/ordering): 2 dong sync_log cho CUNG mot entity co the trung
    /// version (soft-delete update vN, hard-delete sau do cung vN) — pull KHONG duoc lam mat
    /// dong nao, va PHAI giu dung THU TU theo sync_log_id (khong phai (entity_id, version)).
    /// </summary>
    [Fact]
    public async Task Soft_delete_roi_hard_delete_khong_bi_bo_qua_boi_dedup()
    {
        var user = await TestUser.CreateAsync(_fixture, DeviceInfo.Resolve("dev-a", null, null));
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var cursor = await PushOnceAndGetCursorAsync(user, spaceUuid);

        var promptId = Guid.NewGuid();
        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "V1", "C1") } });
        await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { new { promptId, operation = "delete", baseVersion = 1, payload = (object?)null } }
        });

        // Mo phong HARD delete that (chua co duong ghi nao trong app hien tai lam viec nay, vd
        // don dep sau retention/xoa tai khoan trong tuong lai) — trigger AFTER DELETE se ghi mot
        // dong sync_log MOI voi operation="delete" va CUNG version voi ban ghi hien tai (2, vi
        // hard-delete khong tu bump gi them).
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var prompt = await db.Prompts.FirstAsync(p => p.PromptID == promptId);
            db.Prompts.Remove(prompt);
            await db.SaveChangesAsync();
        }

        await AgeSyncLogAsync(spaceUuid);

        // Pull tu thiet bi KHAC de tach bach voi echo suppression (khong lien quan ruling nay).
        using var deviceBClient = await user.CreateAdditionalDeviceClientAsync(_fixture, DeviceInfo.Resolve("dev-b", null, null));
        var response = await deviceBClient.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since={cursor}");
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();

        // insert (v1) + soft-delete/update (v2) + hard-delete (v2) = 3 dong, KHONG dong nao bi
        // drop du 2 dong cuoi trung version.
        Assert.Equal(3, body!.Data!.Changes.Count);
        Assert.Equal(2, body.Data.Changes.Count(c => c.Version == 2));
        Assert.Contains(body.Data.Changes, c => c.Operation == "delete" && c.Version == 2);
        Assert.Contains(body.Data.Changes, c => c.Operation == "update" && c.Version == 2 && c.Prompt!.IsDeleted);
        // Thu tu phai theo sync_log_id: dong hard-delete duoc ghi SAU nen phai o CUOI.
        Assert.Equal("delete", body.Data.Changes[^1].Operation);
    }

    /// <summary>
    /// Ruling Task 3 (an toan cursor, XAC SUAT — xem ghi chu tren SyncService._safetyWindow): mot
    /// dong sync_log vua ghi (con trong _safetyWindow) KHONG duoc tra ve NGAY, va cursor KHONG
    /// duoc tien qua no. Sau khi dong do "du gia", no phai xuat hien binh thuong o lan pull SAU
    /// voi CUNG since.
    /// </summary>
    [Fact]
    public async Task Pull_khong_tra_ve_dong_qua_moi_va_khong_tien_cursor_vuot_qua_no()
    {
        var user = await TestUser.CreateAsync(_fixture, DeviceInfo.Resolve("dev-a", null, null));
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var cursor = await PushOnceAndGetCursorAsync(user, spaceUuid);

        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
            db.SyncLog.Add(new SyncLogEntry
            {
                SpaceID = space.SpaceID,
                EntityType = "prompt",
                EntityID = Guid.NewGuid(),
                Operation = "insert",
                PayloadJson = """{"title":"Vua ghi","content":"C","description":null,"category_id":null,"is_deleted":false}""",
                Version = 1
                // CreatedAt: khong dat -> DB tu gan now() (fix round 1, finding 3a) -> con trong
                // _safetyWindow.
            });
            await db.SaveChangesAsync();
        }

        using var deviceBClient = await user.CreateAdditionalDeviceClientAsync(_fixture, DeviceInfo.Resolve("dev-b", null, null));

        var freshResponse = await deviceBClient.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since={cursor}");
        var freshBody = await freshResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();
        Assert.Empty(freshBody!.Data!.Changes);
        Assert.Equal(cursor, freshBody.Data.ResumeCursor);

        await AgeSyncLogAsync(spaceUuid);

        var agedResponse = await deviceBClient.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since={cursor}");
        var agedBody = await agedResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();
        Assert.Single(agedBody!.Data!.Changes);
        Assert.True(agedBody.Data.ResumeCursor > cursor);
    }

    /// <summary>
    /// Fix round 1, finding 2 (transactional cursor+prompts consistency): ResumeCursor cua CHINH
    /// nhanh snapshot cung phai ap dung _safetyWindow — khong duoc "vuot qua" mot dong vua ghi ma
    /// view REPEATABLE READ dung de doc prompts co the chua kip phan anh.
    /// </summary>
    [Fact]
    public async Task Pull_snapshot_cung_khong_de_cursor_vuot_qua_dong_qua_moi()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);

        long freshId;
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
            var entry = new SyncLogEntry
            {
                SpaceID = space.SpaceID,
                EntityType = "prompt",
                EntityID = Guid.NewGuid(),
                Operation = "insert",
                PayloadJson = "{}",
                Version = 1
                // CreatedAt: khong dat -> DB gan now() -> con qua moi.
            };
            db.SyncLog.Add(entry);
            await db.SaveChangesAsync();
            freshId = entry.SyncLogID;
        }

        var freshResponse = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since=0");
        var freshBody = await freshResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();
        Assert.True(freshBody!.Data!.IsSnapshot);
        Assert.True(freshBody.Data.ResumeCursor < freshId);

        await AgeSyncLogAsync(spaceUuid);

        var agedResponse = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since=0");
        var agedBody = await agedResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();
        Assert.Equal(freshId, agedBody!.Data!.ResumeCursor);
    }

    /// <summary>Ruling P13 + Expo gap G4: retention da het (dong sync_log cu nhat da bi "cron"
    /// xoa) tren mot cursor THAT SU (since &gt; 0) — phai tra snapshot THAT (SnapshotJson day
    /// du), khong duoc tra rong nhu the da dong bo day du. Blob storage kha dung nen van co
    /// BackupSnapshot audit.</summary>
    [Fact]
    public async Task Pull_tra_snapshot_khi_retention_da_het_va_co_blob_storage()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);

        var cursor = await EstablishCursorThenCreateRetentionGapAsync(user, spaceUuid);

        var response = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since={cursor}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();
        Assert.True(body!.Data!.IsSnapshot);
        Assert.NotNull(body.Data.SnapshotJson);
        // G4: noi dung phai NAM THANG trong response (client dung duoc ngay), khong phai mot
        // storage path/URL doi credential Supabase ma client di dong khong co. Prompt A/B da bi
        // "xoa retention" khoi sync_log NHUNG van con SONG trong promptvault.prompts (chua tung xoa) —
        // snapshot phan anh dung TOAN BO trang thai hien tai, khong chi phan con lai trong log.
        Assert.Contains("noi dung A", body.Data.SnapshotJson);
        Assert.Contains("noi dung B", body.Data.SnapshotJson);
        Assert.Contains("noi dung C", body.Data.SnapshotJson);
        Assert.True(body.Data.ResumeCursor > cursor);

        // Blob storage kha dung -> phai co audit BackupSnapshot (day la thanh phan best-effort
        // nhung o day KHONG loi nen PHAI thanh cong).
        using var checkScope = _fixture.CreateScope();
        var checkDb = ApiFixture.Db(checkScope);
        var space2 = await checkDb.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
        Assert.True(await checkDb.BackupSnapshots.CountAsync(b => b.SpaceID == space2.SpaceID && b.SnapshotType == "sync_catchup") >= 1);
    }

    /// <summary>
    /// Fix round 1, finding 4: upload len blob storage la BEST-EFFORT — loi upload KHONG duoc
    /// chan noi dung snapshot tra ve client (client khong doc snapshot qua Storage, chi qua
    /// SnapshotJson). Chi BackupSnapshot (audit) bi anh huong.
    /// </summary>
    [Fact]
    public async Task Pull_van_tra_snapshot_thanh_cong_khi_blob_storage_loi_upload_best_effort()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "V1", "noi dung rieng") } });
        await AgeSyncLogAsync(spaceUuid);

        _fixture.BlobStorage.SimulateUnavailable = true;
        try
        {
            using var beforeScope = _fixture.CreateScope();
            var beforeDb = ApiFixture.Db(beforeScope);
            var space = await beforeDb.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
            var auditCountBefore = await beforeDb.BackupSnapshots.CountAsync(b => b.SpaceID == space.SpaceID);

            var response = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since=0");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();
            Assert.True(body!.Data!.IsSnapshot);
            Assert.Contains("noi dung rieng", body.Data.SnapshotJson);

            using var afterScope = _fixture.CreateScope();
            var afterDb = ApiFixture.Db(afterScope);
            var auditCountAfter = await afterDb.BackupSnapshots.CountAsync(b => b.SpaceID == space.SpaceID);
            // Upload that bai -> KHONG them audit row moi, nhung response cho client van thanh cong.
            Assert.Equal(auditCountBefore, auditCountAfter);
        }
        finally
        {
            // Fake storage la SINGLETON dung chung ca collection test — luon tra ve trang thai
            // binh thuong de khong lam ro cac test chay SAU.
            _fixture.BlobStorage.SimulateUnavailable = false;
        }
    }

    /// <summary>
    /// Fix round 2, finding 4 (con sot lai tu round 1): HttpClient cho Supabase Storage co
    /// Timeout=30s (Program.cs) — khi timeout, .NET nem TaskCanceledException, MOT LOP CON cua
    /// OperationCanceledException, DU request goi PullAsync khong he bi huy. Filter cu trong
    /// SyncService ("ex is not OperationCanceledException") vo tinh de loi timeout NAY thoat khoi
    /// catch va rot thanh 500 chua xu ly — dung diem finding 4 (round 1) yeu cau phai xuong cap
    /// nhe nhang. FakeBlobStorageService.SimulateTimeout mo phong chinh xac tinh huong nay (nem
    /// TaskCanceledException voi mot CancellationToken RIENG, khong lien quan cancellationToken
    /// cua request) — pull van phai tra 200 kem SnapshotJson day du.
    /// </summary>
    [Fact]
    public async Task Pull_van_tra_snapshot_thanh_cong_khi_blob_storage_timeout()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "V1", "noi dung timeout") } });
        await AgeSyncLogAsync(spaceUuid);

        _fixture.BlobStorage.SimulateTimeout = true;
        try
        {
            using var beforeScope = _fixture.CreateScope();
            var beforeDb = ApiFixture.Db(beforeScope);
            var space = await beforeDb.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
            var auditCountBefore = await beforeDb.BackupSnapshots.CountAsync(b => b.SpaceID == space.SpaceID);

            var response = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since=0");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();
            Assert.True(body!.Data!.IsSnapshot);
            Assert.Contains("noi dung timeout", body.Data.SnapshotJson);

            using var afterScope = _fixture.CreateScope();
            var afterDb = ApiFixture.Db(afterScope);
            var auditCountAfter = await afterDb.BackupSnapshots.CountAsync(b => b.SpaceID == space.SpaceID);
            Assert.Equal(auditCountBefore, auditCountAfter);
        }
        finally
        {
            _fixture.BlobStorage.SimulateTimeout = false;
        }
    }

    /// <summary>
    /// Fix round 1, finding 4: cung ket qua voi test tren nhung cho nhanh "chua dang ky"
    /// (IBlobStorageService null — San xuat khi thieu Storage:BaseUrl), khong phai "dang ky
    /// nhung loi". Goi thang SyncService vi ApiFixture luon dang ky FakeBlobStorageService cho
    /// MOI test khac.
    /// </summary>
    [Fact]
    public async Task Pull_van_tra_snapshot_thanh_cong_khi_khong_co_IBlobStorageService_dang_ky()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "V1", "noi dung rieng 2") } });
        await AgeSyncLogAsync(spaceUuid);

        using var scope = _fixture.CreateScope();
        var scopedDb = ApiFixture.Db(scope);
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SyncService>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var space = await scopedDb.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
        var membership = new SpaceMembership(space.SpaceID, spaceUuid, SpaceType.Personal, space.OwnerUserID, user.UserUuid, true);
        var service = new SyncService(scopedDb, new FixedSpaceContext(membership), logger, configuration, blobStorage: null);

        var result = await service.PullAsync(spaceUuid, 0, null, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<SyncPullResponse>(result.Data);
        Assert.True(data.IsSnapshot);
        Assert.Contains("noi dung rieng 2", data.SnapshotJson);

        Assert.Equal(0, await scopedDb.BackupSnapshots.CountAsync(b => b.SpaceID == space.SpaceID));
    }

    /// <summary>Ruling (toi uu, khong bat buoc dung/sai): so dong pending qua nguong (tren mot
    /// cursor THAT SU, since &gt; 0) thi snapshot re hon incremental.</summary>
    [Fact]
    public async Task Pull_tra_snapshot_khi_vuot_nguong_khoi_luong()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var cursor = await PushOnceAndGetCursorAsync(user, spaceUuid);

        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
            for (var i = 0; i < 501; i++)
            {
                db.SyncLog.Add(new SyncLogEntry
                {
                    SpaceID = space.SpaceID,
                    EntityType = "prompt",
                    EntityID = Guid.NewGuid(),
                    Operation = "insert",
                    PayloadJson = "{}",
                    Version = 1,
                    CreatedAt = DateTime.UtcNow.AddSeconds(-30)
                });
            }
            await db.SaveChangesAsync();
        }

        var response = await user.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since={cursor}");
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();

        Assert.True(body!.Data!.IsSnapshot);
        Assert.NotNull(body.Data.SnapshotJson);
    }

    /// <summary>Nguoi ngoai space bi tu choi — cung pattern P17 voi push.</summary>
    [Fact]
    public async Task Nguoi_ngoai_space_bi_tu_choi()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var stranger = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(owner);

        var response = await stranger.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since=0");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
