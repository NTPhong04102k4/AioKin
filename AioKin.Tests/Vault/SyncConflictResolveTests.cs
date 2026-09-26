using System.Net;
using System.Net.Http.Json;
using AioKin.Data.Entities.Sync;
using AioKin.Data.Entities.Vault;
using AioKin.Models.ViewModel.Vault;
using AioKin.Services.Auth.Token;
using AioKin.Tests.Auth;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Vault;

/// <summary>
/// POST /sync/conflicts/{id}/resolve — Task 4. Xem
/// .superpowers/sdd/2026-09-25-promptvault-sync-engine/progress.md muc "Task 4" cho toan bo
/// ruling ma cac test duoi day bam theo: G11 (SECURITY, non-member), P22 (khong Last-Write-Wins,
/// resolve phai fail neu dong song da doi ke tu luc xung dot duoc ghi), G9 (dung/xoa dung cho ca
/// 3 resolution tren xung dot lien quan delete), G8 (response tra NewVersion), MUST carry-forward
/// Task 3 (UpdatedByUserId/UpdatedDeviceId phai la CUA CHINH caller, kiem qua echo suppression).
/// </summary>
[Collection(ApiCollection.Name)]
public class SyncConflictResolveTests
{
    private readonly ApiFixture _fixture;

    public SyncConflictResolveTests(ApiFixture fixture) => _fixture = fixture;

    private static async Task<Guid> GetPersonalSpaceUuidAsync(TestUser user)
    {
        var mine = await user.Client.GetFromJsonAsync<OperationResultOf<List<SpaceResponse>>>("/spaces/me");
        return mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;
    }

    private static object InsertEntry(Guid promptId, string title, string content)
        => new { promptId, operation = "insert", baseVersion = 0, payload = new { title, content } };

    private static object UpdateEntry(Guid promptId, int baseVersion, string title, string content)
        => new { promptId, operation = "update", baseVersion, payload = new { title, content } };

    private static object DeleteEntry(Guid promptId, int baseVersion)
        => new { promptId, operation = "delete", baseVersion, payload = (object?)null };

    /// <summary>Follow-up (tag/variable-only versioning gap): update CHI doi tag, giu nguyen
    /// title/content — dung de dung minh P22 gio "thay" duoc ca loai thay doi nay qua Version
    /// (truoc fix, trigger DB bo qua hoan toan thay doi CHI-tag nen Version dung yen, va mot
    /// resolve dua tren du lieu cu se lot qua kiem tra P22 mot cach sai).</summary>
    private static object TagOnlyUpdateEntry(Guid promptId, int baseVersion, string title, string content, Guid tagId, string tagName)
        => new { promptId, operation = "update", baseVersion, payload = new { title, content, tags = new[] { new { tagId, name = tagName } } } };

    /// <summary>Tao mot xung dot edit-vs-edit don gian: v1 -> deviceB sua thanh "Tu B" (v2) ->
    /// deviceA sua tiep tren baseVersion cu (v1) -> conflict. Tra ve conflictId.</summary>
    private static async Task<Guid> CreateEditVsEditConflictAsync(HttpClient deviceA, HttpClient deviceB, Guid spaceUuid, Guid promptId)
    {
        await deviceA.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "T1", "C1") } });
        await deviceB.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { UpdateEntry(promptId, 1, "Tu B", "noi dung B") } });

        var conflictResponse = await deviceA.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { UpdateEntry(promptId, 1, "Tu A", "noi dung A") } });
        var body = await conflictResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("conflict", body!.Data!.Results[0].Status);
        return body.Data!.Results[0].ConflictId!.Value;
    }

    /// <summary>G9: xung dot local-DELETE-vs-remote-edit — v1 -> deviceB sua thanh "Tu B" (v2)
    /// -> deviceA co gang XOA tren baseVersion cu (v1) -> conflict (LocalOperation="delete",
    /// RemoteIsDeleted=false).</summary>
    private static async Task<Guid> CreateDeleteVsEditConflictAsync(HttpClient deviceA, HttpClient deviceB, Guid spaceUuid, Guid promptId)
    {
        await deviceA.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "T1", "C1") } });
        await deviceB.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { UpdateEntry(promptId, 1, "Tu B", "noi dung B") } });

        var conflictResponse = await deviceA.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { DeleteEntry(promptId, 1) } });
        var body = await conflictResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("conflict", body!.Data!.Results[0].Status);
        return body.Data!.Results[0].ConflictId!.Value;
    }

    // --- Edit-vs-edit (khong lien quan delete) ---

    [Fact]
    public async Task Resolve_keep_local_edit_vs_edit_ghi_de_bang_ban_local()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        var conflictId = await CreateEditVsEditConflictAsync(user.Client, user.Client, spaceUuid, promptId);

        var response = await user.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "keep_local" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<ResolveConflictResponse>>();
        Assert.Equal(3, body!.Data!.NewVersion); // v2 (deviceB) -> content that su doi -> v3
        Assert.False(body.Data!.IsDeleted);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.Equal("Tu A", live.Title);
        Assert.False(live.HasConflict);

        var conflict = await db.SyncConflicts.AsNoTracking().FirstAsync(c => c.ConflictID == conflictId);
        Assert.True(conflict.Resolved);
        Assert.Equal("keep_local", conflict.ResolutionStrategy);
    }

    [Fact]
    public async Task Resolve_keep_remote_edit_vs_edit_giu_nguyen_ban_tren_server()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        var conflictId = await CreateEditVsEditConflictAsync(user.Client, user.Client, spaceUuid, promptId);

        var response = await user.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "keep_remote" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<ResolveConflictResponse>>();
        Assert.Equal(2, body!.Data!.NewVersion); // khong doi noi dung -> version giu nguyen nhu luc conflict duoc ghi

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.Equal("Tu B", live.Title);
        Assert.False(live.HasConflict);
    }

    [Fact]
    public async Task Resolve_merged_edit_vs_edit_ap_dung_noi_dung_tuy_chinh()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        var conflictId = await CreateEditVsEditConflictAsync(user.Client, user.Client, spaceUuid, promptId);

        var response = await user.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new
        {
            resolution = "merged",
            mergedPayload = new { title = "Hop nhat", content = "noi dung hop nhat" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<ResolveConflictResponse>>();
        Assert.Equal(3, body!.Data!.NewVersion);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.Equal("Hop nhat", live.Title);
        Assert.Equal("noi dung hop nhat", live.Content);
        Assert.False(live.HasConflict);
    }

    [Fact]
    public async Task Resolve_merged_thieu_mergedPayload_bi_tu_choi()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        var conflictId = await CreateEditVsEditConflictAsync(user.Client, user.Client, spaceUuid, promptId);

        var response = await user.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "merged" });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var conflict = await db.SyncConflicts.AsNoTracking().FirstAsync(c => c.ConflictID == conflictId);
        Assert.False(conflict.Resolved);
    }

    // --- G11 (SECURITY): non-member ---

    /// <summary>G11: nguoi khong phai thanh vien cua space chua xung dot nay khong duoc resolve.</summary>
    [Fact]
    public async Task Nguoi_ngoai_space_khong_the_resolve_xung_dot()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var stranger = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(owner);
        var promptId = Guid.NewGuid();
        var conflictId = await CreateEditVsEditConflictAsync(owner.Client, owner.Client, spaceUuid, promptId);

        var response = await stranger.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "keep_local" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var conflict = await db.SyncConflicts.AsNoTracking().FirstAsync(c => c.ConflictID == conflictId);
        Assert.False(conflict.Resolved);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.Equal("Tu B", live.Title); // khong bi dong den
    }

    // --- P22: khong Last-Write-Wins ---

    /// <summary>P22: neu dong song da doi TIEP (mot push khac ap dung thanh cong sau khi xung
    /// dot duoc ghi nhan), resolve dua tren du lieu CU phai that bai ro rang, khong duoc am
    /// tham ghi de len thay doi moi hon.</summary>
    [Fact]
    public async Task Resolve_that_bai_neu_dong_song_da_doi_tiep_ke_tu_luc_xung_dot_duoc_ghi()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        var conflictId = await CreateEditVsEditConflictAsync(user.Client, user.Client, spaceUuid, promptId);

        // Sau khi xung dot da duoc ghi (RemoteVersion=2), mot push KHAC (dung baseVersion hien
        // hanh) ap dung thanh cong, day version len 3 -- "remote" ma xung dot dang giu la CU.
        var followUp = await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { UpdateEntry(promptId, 2, "Tu B lan 2", "noi dung B lan 2") } });
        Assert.Equal("applied", (await followUp.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>())!.Data!.Results[0].Status);

        var response = await user.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "keep_local" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var conflict = await db.SyncConflicts.AsNoTracking().FirstAsync(c => c.ConflictID == conflictId);
        Assert.False(conflict.Resolved);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.Equal("Tu B lan 2", live.Title); // khong bi ghi de boi resolve cu
        Assert.Equal(3, live.Version);
    }

    /// <summary>TDD (follow-up tag/variable-only versioning gap): dong SONG doi TIEP sau khi
    /// xung dot duoc ghi, nhung lan nay bang mot thay doi CHI-TAG (khong dong den title/content).
    /// Truoc fix: trigger DB bo qua thay doi CHI-tag (Version dung yen o 2), nen kiem tra P22
    /// (prompt.Version != conflict.RemoteVersion) van thay 2 == 2 va CHO PHEP resolve ghi de len
    /// — am tham xoa mat thay doi tag da xay ra sau do, dung mot the last-writer-wins tran hinh
    /// ma P22 duoc thiet ke de ngan. Sau fix: Prompt.MetaSig doi buoc trigger bump Version len 3,
    /// nen P22 GIO PHAI phat hien va tra 409, giong het truong hop content doi tiep.</summary>
    [Fact]
    public async Task Resolve_that_bai_neu_dong_song_doi_tiep_boi_mot_thay_doi_chi_tag()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        var conflictId = await CreateEditVsEditConflictAsync(user.Client, user.Client, spaceUuid, promptId);

        // Sau khi xung dot da duoc ghi (RemoteVersion=2), mot thiet bi KHAC doi CHI tag tren dong
        // song (baseVersion=2 hien hanh, title/content giu nguyen "Tu B"/"noi dung B").
        var otherTagId = Guid.NewGuid();
        var followUp = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { TagOnlyUpdateEntry(promptId, 2, "Tu B", "noi dung B", otherTagId, "tag-moi") }
        });
        var followUpBody = await followUp.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("applied", followUpBody!.Data!.Results[0].Status);
        // Follow-up: meta_sig doi -> trigger bump Version len 3 du title/content khong doi.
        Assert.Equal(3, followUpBody.Data!.Results[0].NewVersion);

        var response = await user.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "keep_local" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var conflict = await db.SyncConflicts.AsNoTracking().FirstAsync(c => c.ConflictID == conflictId);
        Assert.False(conflict.Resolved);
        var live = await db.Prompts.AsNoTracking()
            .Include(p => p.PromptTags)
            .FirstAsync(p => p.PromptID == promptId);
        Assert.Equal(3, live.Version);
        // Tag them boi thay doi CHI-tag KHONG bi resolve cu am tham ghi de.
        Assert.Single(live.PromptTags);
        Assert.Equal(otherTagId, live.PromptTags.First().TagID);
    }

    // --- G9: xung dot lien quan DELETE ---

    /// <summary>G9: "keep_local" tren local-delete-vs-remote-edit phai THUC SU xoa dong, khong
    /// duoc co gang merge noi dung local vao (vi local von khong co noi dung — no la mot delete).</summary>
    [Fact]
    public async Task Resolve_keep_local_tren_xung_dot_delete_thuc_su_xoa_dong()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        var conflictId = await CreateDeleteVsEditConflictAsync(user.Client, user.Client, spaceUuid, promptId);

        var response = await user.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "keep_local" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<ResolveConflictResponse>>();
        Assert.True(body!.Data!.IsDeleted);
        Assert.Equal(3, body.Data!.NewVersion); // is_deleted false->true la mot content change that

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.True(live.IsDeleted);
        Assert.False(live.HasConflict);
    }

    /// <summary>G9: "keep_remote" tren local-delete-vs-remote-edit phai GIU noi dung edit (khong
    /// bi xoa) — remote chua bao gio la delete trong xung dot nay.</summary>
    [Fact]
    public async Task Resolve_keep_remote_tren_xung_dot_delete_giu_lai_noi_dung_edit()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        var conflictId = await CreateDeleteVsEditConflictAsync(user.Client, user.Client, spaceUuid, promptId);

        var response = await user.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "keep_remote" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<ResolveConflictResponse>>();
        Assert.False(body!.Data!.IsDeleted);
        Assert.Equal(2, body.Data!.NewVersion);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.False(live.IsDeleted);
        Assert.Equal("Tu B", live.Title);
        Assert.False(live.HasConflict);
    }

    /// <summary>G9: "merged" tren local-delete-vs-remote-edit ham y nguoi dung muon dong nay
    /// TON TAI voi noi dung moi -- phai undelete (IsDeleted=false) ap dung MergedPayload.</summary>
    [Fact]
    public async Task Resolve_merged_tren_xung_dot_delete_khoi_phuc_voi_noi_dung_hop_nhat()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        var conflictId = await CreateDeleteVsEditConflictAsync(user.Client, user.Client, spaceUuid, promptId);

        var response = await user.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new
        {
            resolution = "merged",
            mergedPayload = new { title = "Hop nhat sau xoa", content = "noi dung sau xoa" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<ResolveConflictResponse>>();
        Assert.False(body!.Data!.IsDeleted);
        Assert.Equal(3, body.Data!.NewVersion);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.False(live.IsDeleted);
        Assert.Equal("Hop nhat sau xoa", live.Title);
        Assert.False(live.HasConflict);
    }

    /// <summary>G9 (huong nguoc lai): "keep_local" khi CHINH remote dang o trang thai da xoa
    /// (remote-delete-vs-local-edit) phai khoi phuc dong voi noi dung local, khong duoc bo qua
    /// vi dong "da chet".</summary>
    [Fact]
    public async Task Resolve_keep_local_khoi_phuc_dong_da_bi_remote_xoa()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();

        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "T1", "C1") } });
        // "Remote" xoa dong (v1 -> v2, IsDeleted=true).
        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { DeleteEntry(promptId, 1) } });

        // "Local" (tren baseVersion cu = 1) co gang SUA noi dung -> conflict, RemoteIsDeleted=true.
        var conflictResponse = await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { UpdateEntry(promptId, 1, "Tu A local", "noi dung A local") } });
        var conflictBody = await conflictResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("conflict", conflictBody!.Data!.Results[0].Status);
        var conflictId = conflictBody.Data!.Results[0].ConflictId!.Value;

        var response = await user.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "keep_local" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<ResolveConflictResponse>>();
        Assert.False(body!.Data!.IsDeleted);
        Assert.Equal(3, body.Data!.NewVersion);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.False(live.IsDeleted);
        Assert.Equal("Tu A local", live.Title);
    }

    // --- MUST (carry-forward Task 3): UpdatedByUserId/UpdatedDeviceId phai la CUA CHINH caller ---

    /// <summary>
    /// "Lam gia" moi dong sync_log cua mot space qua nguong _safetyWindow (giong helper cung ten
    /// trong SyncPullTests) — mo phong thoi gian troi qua thay vi Task.Delay that.
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
    /// MUST: resolve phai gan UpdatedByUserId/UpdatedDeviceId TU CHINH session cua caller (khong
    /// de sot lai danh tinh nguoi ghi truoc do). Kiem qua pull: thiet bi THUC HIEN resolve
    /// (dev-c) phai KHONG thay lai chinh thay doi cua no (echo suppression dung), con mot thiet
    /// bi KHAC cua CHINH nguoi dung (dev-b, khong lien quan gi den resolve) phai THAY duoc thay
    /// doi do — neu code vo tinh gan sai deviceId (vd giu nguyen dev-a/dev-b tu ban ghi truoc),
    /// mot trong hai ve se sai.
    /// </summary>
    [Fact]
    public async Task Resolve_gan_dung_danh_tinh_caller_echo_suppression_dung_o_lan_pull_ke_tiep()
    {
        var owner = await TestUser.CreateAsync(_fixture, DeviceInfo.Resolve("dev-a", null, null));
        var spaceUuid = await GetPersonalSpaceUuidAsync(owner);
        using var deviceB = await owner.CreateAdditionalDeviceClientAsync(_fixture, DeviceInfo.Resolve("dev-b", null, null));
        using var deviceC = await owner.CreateAdditionalDeviceClientAsync(_fixture, DeviceInfo.Resolve("dev-c", null, null));
        var promptId = Guid.NewGuid();

        var conflictId = await CreateEditVsEditConflictAsync(owner.Client, deviceB, spaceUuid, promptId);

        // Thiet lap mot cursor THAT (khong phai snapshot since=0) truoc khi resolve.
        await AgeSyncLogAsync(spaceUuid);
        var baselinePull = await owner.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since=0");
        var cursor = (await baselinePull.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>())!.Data!.ResumeCursor;

        // Resolve tu THIET BI THU BA (dev-c) — khong phai dev-a (nguoi tao xung dot) hay dev-b.
        var resolveResponse = await deviceC.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "keep_local" });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
        await AgeSyncLogAsync(spaceUuid);

        // dev-c (nguoi THUC HIEN resolve) khong duoc thay lai chinh thay doi cua no.
        var pullFromC = await deviceC.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since={cursor}");
        var changesForC = (await pullFromC.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>())!.Data!.Changes;
        Assert.DoesNotContain(changesForC, c => c.EntityId == promptId);

        // dev-b (thiet bi KHAC cua CUNG nguoi dung, khong lien quan resolve) phai THAY duoc.
        var pullFromB = await deviceB.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since={cursor}");
        var changesForB = (await pullFromB.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>())!.Data!.Changes;
        var change = Assert.Single(changesForB, c => c.EntityId == promptId);
        Assert.Equal("Tu A", change.Prompt!.Title);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.Equal(owner.UserId, live.UpdatedByUserId);
        Assert.Equal("dev-c", live.UpdatedDeviceId);
    }

    // --- Fix round 1 (Task 4 review): P12 phai duoc ap dung trong resolve giong het push ---

    /// <summary>
    /// Fix round 1 + follow-up meta_sig: mot resolve "merged" GIU NGUYEN title/content/category
    /// cua remote (khong cot NOI DUNG nao cua bang prompts thuc su doi) nhung DOI tag phai van
    /// toi duoc thiet bi khac qua pull. Truoc follow-up nay, trigger DB khong bump version (khong
    /// cot prompts "noi dung" nao doi, va meta_sig chua ton tai) nen khong tu ghi sync_log —
    /// SyncService phai tu ghi thu cong (P12 workaround). Sau follow-up: Prompt.MetaSig doi (tag
    /// them vao) buoc trigger tu bump version + tu ghi sync_log, workaround thu cong da bo (xem
    /// SyncService.ComputeMetaSig) — cung 1 dong sync_log MOI duoc ghi (khong trung lap).
    /// </summary>
    [Fact]
    public async Task Resolve_merged_giu_nguyen_noi_dung_nhung_doi_tag_van_toi_duoc_thiet_bi_khac_qua_pull()
    {
        var owner = await TestUser.CreateAsync(_fixture, DeviceInfo.Resolve("dev-a", null, null));
        var spaceUuid = await GetPersonalSpaceUuidAsync(owner);
        using var deviceB = await owner.CreateAdditionalDeviceClientAsync(_fixture, DeviceInfo.Resolve("dev-b", null, null));
        using var deviceC = await owner.CreateAdditionalDeviceClientAsync(_fixture, DeviceInfo.Resolve("dev-c", null, null));
        var promptId = Guid.NewGuid();

        // Sau ham nay: remote (deviceB) = "Tu B"/"noi dung B", v2.
        var conflictId = await CreateEditVsEditConflictAsync(owner.Client, deviceB, spaceUuid, promptId);

        await AgeSyncLogAsync(spaceUuid);
        var baselinePull = await owner.Client.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since=0");
        var cursor = (await baselinePull.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>())!.Data!.ResumeCursor;

        var tagId = Guid.NewGuid();
        var logCountBeforeResolve = await CountSyncLogAsync(promptId);

        // "merged" GIU NGUYEN title/content cua remote y het -- CHI them mot tag moi.
        var resolveResponse = await owner.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new
        {
            resolution = "merged",
            mergedPayload = new { title = "Tu B", content = "noi dung B", tags = new[] { new { tagId, name = "tag-moi" } } }
        });

        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
        var resolveBody = await resolveResponse.Content.ReadFromJsonAsync<OperationResultOf<ResolveConflictResponse>>();
        // Follow-up: meta_sig doi (tag them vao) -> trigger GIO bump version du noi dung khong doi.
        Assert.Equal(3, resolveBody!.Data!.NewVersion);

        await AgeSyncLogAsync(spaceUuid);

        // deviceC khong lien quan gi den resolve nay -- phai THAY duoc thay doi tag qua pull, voi
        // Tags duoc hydrate dung (giong Task 3's tag-only-push pull test).
        var pullResponse = await deviceC.GetAsync($"/sync/pull?spaceUuid={spaceUuid}&since={cursor}");
        var pullBody = await pullResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPullResponse>>();

        var change = Assert.Single(pullBody!.Data!.Changes, c => c.EntityId == promptId);
        // Follow-up: dong nay gio duoc CHINH trigger DB ghi (payload la to_jsonb(NEW), khong con
        // "kind":"tags_variables" cua workaround thu cong cu) nen TagsVariablesOnly la false —
        // Title/Content van dung du vi day la snapshot HANG THAT tai thoi diem do.
        Assert.False(change.TagsVariablesOnly);
        Assert.Contains("tag-moi", change.Prompt!.Tags);
        Assert.Equal("Tu B", change.Prompt.Title);
        Assert.Equal("noi dung B", change.Prompt.Content);

        // Dung 1 dong sync_log MOI duoc them boi resolve nay (tu trigger) — khong bi trung lap
        // boi workaround thu cong cu (da bo).
        Assert.Equal(logCountBeforeResolve + 1, await CountSyncLogAsync(promptId));
    }

    /// <summary>
    /// Follow-up (description omit=unchanged, TDD cho bug mat du lieu): "merged" o day khong gui
    /// description trong MergedPayload -- theo dung ngu nghia moi (omit = giu nguyen, giong het
    /// SyncPushTests.Bo_qua_categoryId_giu_nguyen_ClearCategory_thi_xoa), description HIEN CO cua
    /// dong prompt (remote tai thoi diem resolve) KHONG duoc am tham xoa.
    /// </summary>
    [Fact]
    public async Task Resolve_merged_bo_qua_description_giu_nguyen_description_hien_co()
    {
        var owner = await TestUser.CreateAsync(_fixture, DeviceInfo.Resolve("dev-a", null, null));
        var spaceUuid = await GetPersonalSpaceUuidAsync(owner);
        using var deviceB = await owner.CreateAdditionalDeviceClientAsync(_fixture, DeviceInfo.Resolve("dev-b", null, null));
        var promptId = Guid.NewGuid();

        await owner.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { new { promptId, operation = "insert", baseVersion = 0, payload = new { title = "T1", content = "C1", description = "Mo ta ban dau" } } }
        });
        await deviceB.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { UpdateEntry(promptId, 1, "Tu B", "noi dung B") } });

        var conflictResponse = await owner.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { UpdateEntry(promptId, 1, "Tu A", "noi dung A") } });
        var conflictBody = await conflictResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("conflict", conflictBody!.Data!.Results[0].Status);
        var conflictId = conflictBody.Data!.Results[0].ConflictId!.Value;

        var resolveResponse = await owner.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new
        {
            resolution = "merged",
            mergedPayload = new { title = "Hop nhat", content = "noi dung hop nhat" }
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var detail = await owner.Client.GetFromJsonAsync<OperationResultOf<PromptDetailResponse>>($"/prompts/{promptId}?spaceUuid={spaceUuid}");
        Assert.Equal("Mo ta ban dau", detail!.Data!.Description);
    }

    /// <summary>
    /// Follow-up (description omit=unchanged): "keep_local" ap dung lai CHINH LocalPayloadJson da
    /// duoc luu luc push bi conflict -- neu payload do (dung quy uoc omit=giu nguyen) khong gui
    /// description, description HIEN CO cua dong prompt khong duoc am tham xoa.
    /// </summary>
    [Fact]
    public async Task Resolve_keep_local_bo_qua_description_giu_nguyen_description_hien_co()
    {
        var owner = await TestUser.CreateAsync(_fixture, DeviceInfo.Resolve("dev-a", null, null));
        var spaceUuid = await GetPersonalSpaceUuidAsync(owner);
        using var deviceB = await owner.CreateAdditionalDeviceClientAsync(_fixture, DeviceInfo.Resolve("dev-b", null, null));
        var promptId = Guid.NewGuid();

        await owner.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { new { promptId, operation = "insert", baseVersion = 0, payload = new { title = "T1", content = "C1", description = "Mo ta ban dau" } } }
        });
        await deviceB.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { UpdateEntry(promptId, 1, "Tu B", "noi dung B") } });

        // Local (owner) update KHONG gui description trong payload.
        var conflictResponse = await owner.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { UpdateEntry(promptId, 1, "Tu A", "noi dung A") } });
        var conflictBody = await conflictResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("conflict", conflictBody!.Data!.Results[0].Status);
        var conflictId = conflictBody.Data!.Results[0].ConflictId!.Value;

        var resolveResponse = await owner.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "keep_local" });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var detail = await owner.Client.GetFromJsonAsync<OperationResultOf<PromptDetailResponse>>($"/prompts/{promptId}?spaceUuid={spaceUuid}");
        Assert.Equal("Mo ta ban dau", detail!.Data!.Description);
    }

    private async Task<int> CountSyncLogAsync(Guid entityId)
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        return await db.SyncLog.CountAsync(l => l.EntityID == entityId);
    }

    // --- Finding 1 (final review, USER DECISION MADE): "keep_local"/"merged" GHI DE noi dung --
    // cung dieu kien voi push: chi tac gia hoac CanManage moi duoc resolve mot xung dot theo
    // huong ghi de noi dung cua NGUOI KHAC. "keep_remote" la no-op nen khong bi gioi han nay. ---

    private async Task<Guid> CreateTeamSpaceAsync(params (Guid userId, SpaceMemberRole role)[] members)
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var space = new Space { SpaceType = SpaceType.Team, Name = "Team Resolve Test", OwnerUserID = members[0].userId };
        db.Spaces.Add(space);
        foreach (var (userId, role) in members)
            db.SpaceMembers.Add(new SpaceMember { SpaceID = space.SpaceID, UserID = userId, MemberRole = role });
        await db.SaveChangesAsync();
        return space.SpaceUUID;
    }

    /// <summary>Finding 1: thanh vien thuong (khong phai tac gia, khong CanManage) khong duoc
    /// "keep_local" mot xung dot ma noi dung "local" thuoc VE NGUOI KHAC (tac gia).</summary>
    [Fact]
    public async Task Thanh_vien_thuong_khong_duoc_keep_local_xung_dot_cua_nguoi_khac()
    {
        var author = await TestUser.CreateAsync(_fixture);
        var otherMember = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await CreateTeamSpaceAsync(
            (author.UserId, SpaceMemberRole.Member),
            (otherMember.UserId, SpaceMemberRole.Member));
        var promptId = Guid.NewGuid();
        var conflictId = await CreateEditVsEditConflictAsync(author.Client, author.Client, spaceUuid, promptId);

        var response = await otherMember.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "keep_local" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var conflict = await db.SyncConflicts.AsNoTracking().FirstAsync(c => c.ConflictID == conflictId);
        Assert.False(conflict.Resolved);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.Equal("Tu B", live.Title); // khong bi dong den
    }

    /// <summary>Cung ly do voi keep_local, cho resolution "merged".</summary>
    [Fact]
    public async Task Thanh_vien_thuong_khong_duoc_merged_xung_dot_cua_nguoi_khac()
    {
        var author = await TestUser.CreateAsync(_fixture);
        var otherMember = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await CreateTeamSpaceAsync(
            (author.UserId, SpaceMemberRole.Member),
            (otherMember.UserId, SpaceMemberRole.Member));
        var promptId = Guid.NewGuid();
        var conflictId = await CreateEditVsEditConflictAsync(author.Client, author.Client, spaceUuid, promptId);

        var response = await otherMember.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new
        {
            resolution = "merged",
            mergedPayload = new { title = "Chiem doat", content = "noi dung chiem doat" }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var conflict = await db.SyncConflicts.AsNoTracking().FirstAsync(c => c.ConflictID == conflictId);
        Assert.False(conflict.Resolved);
    }

    /// <summary>Finding 1: "keep_remote" khong ghi de gi ca (P22 da xac nhan dong song CHINH LA
    /// remote duoc ghi nhan) nen bat ky thanh vien nao thay duoc xung dot van resolve duoc, ke
    /// ca khong phai tac gia/CanManage.</summary>
    [Fact]
    public async Task Thanh_vien_thuong_van_duoc_keep_remote_xung_dot_cua_nguoi_khac()
    {
        var author = await TestUser.CreateAsync(_fixture);
        var otherMember = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await CreateTeamSpaceAsync(
            (author.UserId, SpaceMemberRole.Member),
            (otherMember.UserId, SpaceMemberRole.Member));
        var promptId = Guid.NewGuid();
        var conflictId = await CreateEditVsEditConflictAsync(author.Client, author.Client, spaceUuid, promptId);

        var response = await otherMember.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "keep_remote" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var conflict = await db.SyncConflicts.AsNoTracking().FirstAsync(c => c.ConflictID == conflictId);
        Assert.True(conflict.Resolved);
    }

    /// <summary>Finding 1: Owner/Admin (CanManage) duoc "keep_local" xung dot cua NGUOI KHAC --
    /// ngoai le duy nhat cua gioi han moi, dung y het push.</summary>
    [Fact]
    public async Task Admin_duoc_keep_local_xung_dot_cua_nguoi_khac()
    {
        var author = await TestUser.CreateAsync(_fixture);
        var admin = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await CreateTeamSpaceAsync(
            (author.UserId, SpaceMemberRole.Member),
            (admin.UserId, SpaceMemberRole.Admin));
        var promptId = Guid.NewGuid();
        var conflictId = await CreateEditVsEditConflictAsync(author.Client, author.Client, spaceUuid, promptId);

        var response = await admin.Client.PostAsJsonAsync($"/sync/conflicts/{conflictId}/resolve", new { resolution = "keep_local" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var conflict = await db.SyncConflicts.AsNoTracking().FirstAsync(c => c.ConflictID == conflictId);
        Assert.True(conflict.Resolved);
    }
}
