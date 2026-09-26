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
/// POST /sync/push — duy nhat mot duong ghi (create/update/delete) cho Prompt, kem Git-style
/// conflict detection. Xem
/// .superpowers/sdd/2026-09-25-promptvault-sync-engine/progress.md muc "Task 2" cho toan bo
/// ruling ma cac test duoi day bam theo.
/// </summary>
[Collection(ApiCollection.Name)]
public class SyncPushTests
{
    private readonly ApiFixture _fixture;

    public SyncPushTests(ApiFixture fixture) => _fixture = fixture;

    private static async Task<Guid> GetPersonalSpaceUuidAsync(TestUser user)
    {
        var mine = await user.Client.GetFromJsonAsync<OperationResultOf<List<SpaceResponse>>>("/spaces/me");
        return mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;
    }

    private static object InsertEntry(Guid promptId, string title, string content, object? tags = null, object? variables = null, Guid? categoryId = null, string? categoryName = null)
        => new
        {
            promptId,
            operation = "insert",
            baseVersion = 0,
            payload = new { title, content, categoryId, categoryName, tags, variables }
        };

    private static object UpdateEntry(Guid promptId, int baseVersion, string title, string content, object? tags = null, object? variables = null, Guid? categoryId = null, string? categoryName = null)
        => new
        {
            promptId,
            operation = "update",
            baseVersion,
            payload = new { title, content, categoryId, categoryName, tags, variables }
        };

    [Fact]
    public async Task Insert_moi_thanh_cong_voi_id_client_sinh()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();

        var response = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { InsertEntry(promptId, "Caption skincare", "Viet caption {product_name}") }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("applied", body!.Data!.Results[0].Status);
        Assert.Equal(1, body.Data!.Results[0].NewVersion);
    }

    [Fact]
    public async Task Update_dung_baseVersion_thi_ap_dung_va_tang_version()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();

        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "V1", "noi dung v1") } });

        var response = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { UpdateEntry(promptId, 1, "V2", "noi dung v2") }
        });

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("applied", body!.Data!.Results[0].Status);
        // Carry-forward Task 1: Version == 2 trong response API sau 1 sua doi noi dung that su.
        Assert.Equal(2, body.Data!.Results[0].NewVersion);
    }

    /// <summary>P9: prompt phai duoc nap TRACKED truoc khi set HasConflict + save, khong thi
    /// mutation se khong bao gio duoc luu — kiem tra qua chinh live row trong DB.</summary>
    [Fact]
    public async Task Update_sai_baseVersion_thi_tra_conflict_khong_dung_live_row()
    {
        var owner = await TestUser.CreateAsync(_fixture, DeviceInfo.Resolve("dev-a", null, null));
        var spaceUuid = await GetPersonalSpaceUuidAsync(owner);
        var promptId = Guid.NewGuid();
        using var deviceBClient = await owner.CreateAdditionalDeviceClientAsync(_fixture, DeviceInfo.Resolve("dev-b", null, null));

        await owner.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "V1", "noi dung goc") } });

        // Thiet bi B day version len 2 truoc.
        await deviceBClient.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { UpdateEntry(promptId, 1, "V2 tu device B", "noi dung device B") }
        });

        // Thiet bi A push tiep voi baseVersion = 1 (cu) — phai conflict.
        var response = await owner.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { UpdateEntry(promptId, 1, "V2 tu device A", "noi dung device A") }
        });

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("conflict", body!.Data!.Results[0].Status);
        Assert.Equal("V2 tu device B", body.Data!.Results[0].Remote!.Title);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.Equal("V2 tu device B", live.Title);
        Assert.True(live.HasConflict);
    }

    /// <summary>P17 (SECURITY): nguoi khong thuoc space bi tu choi 403, khong duoc push gi ca.</summary>
    [Fact]
    public async Task Nguoi_ngoai_space_bi_tu_choi()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var stranger = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(owner);

        var response = await stranger.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { InsertEntry(Guid.NewGuid(), "T", "C") }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Carry-forward MUST (space_id trong conditional update): mot thanh vien THAT cua space A
    /// khong duoc "update" mot promptId thuc chat thuoc space B bang cach ghep spaceUuid cua A.
    /// </summary>
    [Fact]
    public async Task Push_update_toi_prompt_thuoc_space_khac_bi_tu_choi_chung_chung()
    {
        var ownerA = await TestUser.CreateAsync(_fixture);
        var ownerB = await TestUser.CreateAsync(_fixture);
        var spaceAUuid = await GetPersonalSpaceUuidAsync(ownerA);
        var spaceBUuid = await GetPersonalSpaceUuidAsync(ownerB);

        Guid promptIdFromB;
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var spaceB = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceBUuid);
            promptIdFromB = Guid.NewGuid();
            db.Prompts.Add(new Prompt { PromptID = promptIdFromB, SpaceID = spaceB.SpaceID, AuthorUserID = ownerB.UserId, Title = "Bi mat B", Content = "Noi dung B" });
            await db.SaveChangesAsync();
        }

        var response = await ownerA.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid = spaceAUuid,
            entities = new[] { UpdateEntry(promptIdFromB, 1, "Chiem doat", "Noi dung chiem doat") }
        });

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("rejected", body!.Data!.Results[0].Status);

        using var checkScope = _fixture.CreateScope();
        var checkDb = ApiFixture.Db(checkScope);
        var untouched = await checkDb.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptIdFromB);
        Assert.Equal("Bi mat B", untouched.Title);
    }

    /// <summary>Carry-forward MUST: insert voi PromptId trung mot dong da ton tai o space KHAC
    /// phai bi tu choi chung chung, khong duoc am tham vi pham khoa chinh roi rot xuong 500.</summary>
    [Fact]
    public async Task Push_insert_trung_id_voi_prompt_o_space_khac_bi_tu_choi()
    {
        var ownerA = await TestUser.CreateAsync(_fixture);
        var ownerB = await TestUser.CreateAsync(_fixture);
        var spaceAUuid = await GetPersonalSpaceUuidAsync(ownerA);
        var spaceBUuid = await GetPersonalSpaceUuidAsync(ownerB);

        Guid collidingId;
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var spaceB = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceBUuid);
            collidingId = Guid.NewGuid();
            db.Prompts.Add(new Prompt { PromptID = collidingId, SpaceID = spaceB.SpaceID, AuthorUserID = ownerB.UserId, Title = "Cua B", Content = "Noi dung B" });
            await db.SaveChangesAsync();
        }

        var response = await ownerA.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid = spaceAUuid,
            entities = new[] { InsertEntry(collidingId, "Cua A", "Noi dung A") }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("rejected", body!.Data!.Results[0].Status);
    }

    /// <summary>Carry-forward MUST (category/tag cung-space): categoryId ton tai nhung o space
    /// khac phai bi tu choi chung chung (khong lo no ton tai o noi khac), khong duoc 500.</summary>
    [Fact]
    public async Task Push_voi_categoryId_thuoc_space_khac_bi_tu_choi()
    {
        var ownerA = await TestUser.CreateAsync(_fixture);
        var ownerB = await TestUser.CreateAsync(_fixture);
        var spaceAUuid = await GetPersonalSpaceUuidAsync(ownerA);
        var spaceBUuid = await GetPersonalSpaceUuidAsync(ownerB);

        Guid categoryIdFromB;
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var spaceB = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceBUuid);
            categoryIdFromB = Guid.NewGuid();
            db.Categories.Add(new Category { CategoryID = categoryIdFromB, SpaceID = spaceB.SpaceID, Name = "Cua B" });
            await db.SaveChangesAsync();
        }

        var response = await ownerA.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid = spaceAUuid,
            entities = new[] { InsertEntry(Guid.NewGuid(), "T", "C", categoryId: categoryIdFromB, categoryName: "Cua B") }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("rejected", body!.Data!.Results[0].Status);
    }

    /// <summary>Cung ruling voi category nhung cho tag.</summary>
    [Fact]
    public async Task Push_voi_tagId_thuoc_space_khac_bi_tu_choi()
    {
        var ownerA = await TestUser.CreateAsync(_fixture);
        var ownerB = await TestUser.CreateAsync(_fixture);
        var spaceAUuid = await GetPersonalSpaceUuidAsync(ownerA);
        var spaceBUuid = await GetPersonalSpaceUuidAsync(ownerB);

        Guid tagIdFromB;
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var spaceB = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceBUuid);
            tagIdFromB = Guid.NewGuid();
            db.Tags.Add(new Tag { TagID = tagIdFromB, SpaceID = spaceB.SpaceID, Name = "cua-b" });
            await db.SaveChangesAsync();
        }

        var response = await ownerA.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid = spaceAUuid,
            entities = new[] { InsertEntry(Guid.NewGuid(), "T", "C", tags: new[] { new { tagId = tagIdFromB, name = "cua-b" } }) }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("rejected", body!.Data!.Results[0].Status);
    }

    /// <summary>Ruling: tao category/tag moi phai doi chieu (SpaceID, Name) voi unique index —
    /// gui 2 lan cung ten trong cung space khong duoc tao 2 dong trung.</summary>
    [Fact]
    public async Task Category_trung_ten_trong_cung_space_duoc_tai_su_dung_khong_tao_trung()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);

        var firstCategoryId = Guid.NewGuid();
        await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { InsertEntry(Guid.NewGuid(), "T1", "C1", categoryId: firstCategoryId, categoryName: "Marketing") }
        });

        // Thiet bi/prompt khac tu sinh MOT id category MOI nhung TRUNG TEN — phai duoc gan vao
        // dong da co, khong duoc tao dong thu hai (se vo unique index (SpaceID, Name)).
        var secondCategoryId = Guid.NewGuid();
        var response = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { InsertEntry(Guid.NewGuid(), "T2", "C2", categoryId: secondCategoryId, categoryName: "Marketing") }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("applied", body!.Data!.Results[0].Status);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
        Assert.Equal(1, await db.Categories.CountAsync(c => c.SpaceID == space.SpaceID && c.Name == "Marketing"));
    }

    /// <summary>Follow-up (tag/variable-only versioning gap): push CHI doi tag (noi dung prompt
    /// giu nguyen) GIO PHAI bump version + ghi DUNG 1 dong sync_log moi qua trigger DB — truoc
    /// day (P12) trigger vault.fn_prompts_before_update bo qua hoan toan thay doi loai nay (chi
    /// title/content/description/category_id/is_deleted nam trong WHEN clause), nen 2 thiet bi
    /// cung sua tag tu CUNG mot baseVersion se ca hai "thanh cong" trong im lang (last-writer-wins
    /// khong ai biet) — xem Update_hai_thiet_bi_cung_doi_tag_tu_cung_baseVersion_thi_conflict ben
    /// duoi cho kich ban 2-thiet-bi day du. Test nay chi xac nhan phan mong: 1 push tag-only DON
    /// LE gio bump version dung 1 lan (khong bi trung do ca trigger LAN AddTagVariableSyncLogEntry
    /// cu (da bo) cung ghi — se la 2 dong sync_log cho 1 thay doi neu con giu ca hai).</summary>
    [Fact]
    public async Task Push_chi_doi_tag_gio_bump_version_qua_trigger_khong_ghi_trung_sync_log()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();

        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "T1", "C1") } });

        var tagId = Guid.NewGuid();
        var response = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { UpdateEntry(promptId, 1, "T1", "C1", tags: new[] { new { tagId, name = "moi" } }) }
        });

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("applied", body!.Data!.Results[0].Status);
        // Follow-up: meta_sig doi (tag them vao) -> trigger vault.fn_prompts_before_update GIO
        // bump version, du title/content khong doi.
        Assert.Equal(2, body.Data!.Results[0].NewVersion);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var logs = await db.SyncLog.Where(l => l.EntityID == promptId).OrderBy(l => l.SyncLogID).ToListAsync();
        // Dung 1 dong tu trigger luc insert + 1 dong tu CHINH trigger cho update nay — KHONG con
        // dong thu 2 trung lap tu AddTagVariableSyncLogEntry (da bo, xem SyncService.ComputeMetaSig).
        Assert.Equal(2, logs.Count);
        Assert.Equal("update", logs[^1].Operation);
        Assert.Equal(2, logs[^1].Version);

        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.NotNull(live.MetaSig);
    }

    /// <summary>TDD (follow-up tag/variable-only versioning gap): 2 thiet bi CUNG doi CHI tag tu
    /// CUNG mot baseVersion khong con duoc phep ca hai "thanh cong" trong im lang — thiet bi thu
    /// hai phai nhan "conflict" giong het mot xung dot noi dung binh thuong, nho Prompt.MetaSig
    /// lam trigger DB bump version cho ca thay doi CHI-tag.</summary>
    [Fact]
    public async Task Update_hai_thiet_bi_cung_doi_tag_tu_cung_baseVersion_thi_conflict()
    {
        var owner = await TestUser.CreateAsync(_fixture, DeviceInfo.Resolve("dev-a", null, null));
        var spaceUuid = await GetPersonalSpaceUuidAsync(owner);
        var promptId = Guid.NewGuid();
        using var deviceBClient = await owner.CreateAdditionalDeviceClientAsync(_fixture, DeviceInfo.Resolve("dev-b", null, null));

        await owner.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "T1", "C1") } });

        var tagFromA = Guid.NewGuid();
        var tagFromB = Guid.NewGuid();

        // Thiet bi A doi CHI tag truoc, tu baseVersion=1 -> phai thanh cong va bump version len 2
        // (truoc fix: van la 1, vi trigger "mu" voi thay doi CHI-tag).
        var responseA = await owner.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { UpdateEntry(promptId, 1, "T1", "C1", tags: new[] { new { tagId = tagFromA, name = "tu-a" } }) }
        });
        var bodyA = await responseA.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("applied", bodyA!.Data!.Results[0].Status);
        Assert.Equal(2, bodyA.Data!.Results[0].NewVersion);

        // Thiet bi B, KHONG biet gi ve push cua A, cung doi CHI tag tu CUNG baseVersion=1 —
        // truoc fix day se "thanh cong" trong im lang (last-writer-wins tren tag, de mat tag cua
        // A khong dau vet). Sau fix: phai la conflict, vi Version that su cua dong da la 2.
        var responseB = await deviceBClient.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { UpdateEntry(promptId, 1, "T1", "C1", tags: new[] { new { tagId = tagFromB, name = "tu-b" } }) }
        });
        var bodyB = await responseB.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("conflict", bodyB!.Data!.Results[0].Status);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking()
            .Include(p => p.PromptTags)
            .FirstAsync(p => p.PromptID == promptId);

        Assert.True(live.HasConflict);
        Assert.Equal(2, live.Version);
        // Tag cua A van con nguyen — B KHONG duoc am tham ghi de.
        Assert.Single(live.PromptTags);
        Assert.Equal(tagFromA, live.PromptTags.First().TagID);
    }

    /// <summary>TDD (follow-up tag/variable-only versioning gap): mot push CHI doi noi dung
    /// (title/content), KHONG dong den tags/variables (ca hai field deu omit/null trong payload),
    /// phai KHONG bi anh huong boi thay doi nay — Prompt.MetaSig phai giu NGUYEN (SyncService chi
    /// tinh lai no khi tagsProvided/variablesProvided), va version chi bump DUNG 1 lan (tu WHEN
    /// clause cu title/content, khong phai bump 2 lan hay bump sai vi mot lan tinh lai MetaSig
    /// khong can thiet lam no "trong nhu co doi").</summary>
    [Fact]
    public async Task Push_chi_doi_noi_dung_khong_dong_tag_thi_meta_sig_giu_nguyen_khong_bump_thua()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        var tagId = Guid.NewGuid();

        // Insert co san 1 tag, de MetaSig ban dau phan anh dung tag do (khac hash rong).
        await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { InsertEntry(promptId, "T1", "C1", tags: new[] { new { tagId, name = "giu-nguyen" } }) }
        });

        using (var scopeBefore = _fixture.CreateScope())
        {
            var dbBefore = ApiFixture.Db(scopeBefore);
            var beforeUpdate = await dbBefore.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
            Assert.NotNull(beforeUpdate.MetaSig);

            var response = await user.Client.PostAsJsonAsync("/sync/push", new
            {
                spaceUuid,
                // tags/variables omit -> null -> "giu nguyen", dung ngu nghia G12.
                entities = new[] { UpdateEntry(promptId, 1, "T2", "C2") }
            });

            var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
            Assert.Equal("applied", body!.Data!.Results[0].Status);
            // Chi bump 1 lan (tu title/content doi) — khong phai 2 lan.
            Assert.Equal(2, body.Data!.Results[0].NewVersion);

            using var scopeAfter = _fixture.CreateScope();
            var dbAfter = ApiFixture.Db(scopeAfter);
            var afterUpdate = await dbAfter.Prompts.AsNoTracking()
                .Include(p => p.PromptTags)
                .FirstAsync(p => p.PromptID == promptId);

            Assert.Equal(beforeUpdate.MetaSig, afterUpdate.MetaSig);
            Assert.Single(afterUpdate.PromptTags);
            Assert.Equal(tagId, afterUpdate.PromptTags.First().TagID);
        }
    }

    /// <summary>G10: insert bi retry (response lan truoc bi mat) voi noi dung giong het phai
    /// duoc coi la thanh cong khong lam gi them, KHONG duoc bao la conflict moi.</summary>
    [Fact]
    public async Task Retry_insert_giong_het_duoc_coi_la_thanh_cong_khong_tao_conflict()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        var entry = InsertEntry(promptId, "Tieu de", "Noi dung");

        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { entry } });

        // Retry: dung y het promptId + payload nhu lan dau (client tuong response bi mat).
        var retryResponse = await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { entry } });

        var body = await retryResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("applied", body!.Data!.Results[0].Status);
        Assert.Equal(1, body.Data!.Results[0].NewVersion);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.False(live.HasConflict);
        Assert.Equal(0, await db.SyncConflicts.CountAsync(c => c.EntityID == promptId));
    }

    /// <summary>Expo gap G12: field tags/variables bi OMIT (khong gui) nghia la giu nguyen; chi
    /// mang rong TUONG MINH [] moi xoa het.</summary>
    [Fact]
    public async Task Bo_qua_truong_tags_giu_nguyen_gui_mang_rong_thi_xoa()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        var tagId = Guid.NewGuid();

        await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { InsertEntry(promptId, "T1", "C1", tags: new[] { new { tagId, name = "giu-lai" } }) }
        });

        // Update KHONG gui truong "tags" (omit that su — anonymous object khong co property nay).
        var responseOmit = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { new { promptId, operation = "update", baseVersion = 1, payload = new { title = "T2", content = "C2" } } }
        });
        Assert.Equal("applied", (await responseOmit.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>())!.Data!.Results[0].Status);

        var detailAfterOmit = await user.Client.GetFromJsonAsync<OperationResultOf<PromptDetailResponse>>($"/prompts/{promptId}?spaceUuid={spaceUuid}");
        Assert.Contains("giu-lai", detailAfterOmit!.Data!.Tags);

        // Update gui mang rong TUONG MINH -> phai xoa het.
        var responseClear = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { UpdateEntry(promptId, 2, "T3", "C3", tags: Array.Empty<object>()) }
        });
        Assert.Equal("applied", (await responseClear.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>())!.Data!.Results[0].Status);

        var detailAfterClear = await user.Client.GetFromJsonAsync<OperationResultOf<PromptDetailResponse>>($"/prompts/{promptId}?spaceUuid={spaceUuid}");
        Assert.Empty(detailAfterClear!.Data!.Tags);
    }

    /// <summary>P11 (carry-forward Task 1): soft-delete phai bump version giong mot content
    /// change that su, de mot edit den sau tren base_version cu bi nhan dung la conflict.</summary>
    [Fact]
    public async Task Xoa_mem_bump_version()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();

        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "T1", "C1") } });

        var response = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { new { promptId, operation = "delete", baseVersion = 1, payload = (object?)null } }
        });

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("applied", body!.Data!.Results[0].Status);
        Assert.Equal(2, body.Data!.Results[0].NewVersion);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.True(live.IsDeleted);
        Assert.Equal(2, live.Version);
    }

    /// <summary>Ruling per-entry validation: 1 entry loi (payload null cho operation="update")
    /// khong duoc lam bung 500 hay huy ca batch — entry con lai van phai duoc ap dung.</summary>
    [Fact]
    public async Task Entry_malformed_bi_tu_choi_rieng_khong_anh_huong_entry_khac_trong_batch()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var goodPromptId = Guid.NewGuid();
        var badPromptId = Guid.NewGuid();

        var response = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new object[]
            {
                new { promptId = badPromptId, operation = "update", baseVersion = 0, payload = (object?)null },
                InsertEntry(goodPromptId, "OK", "Noi dung OK")
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal(2, body!.Data!.Results.Count);
        Assert.Equal("rejected", body.Data!.Results[0].Status);
        Assert.Equal("applied", body.Data!.Results[1].Status);

        // Fix round 1, finding 5: tong ket cap-batch phai phan anh dung ca 2 entry.
        Assert.Equal(1, body.Data.AppliedCount);
        Assert.Equal(1, body.Data.RejectedCount);
        Assert.Equal(0, body.Data.ConflictCount);
        Assert.True(body.Data.HasFailures);
    }

    /// <summary>
    /// Fix round 1, finding 1 (repro dung y nguyen tu adversarial review): 2 tag MOI trung
    /// TagID trong CUNG 1 entry truoc day lam EF nem InvalidOperationException ngay tai Add()
    /// (truoc ca SaveChangesAsync) -> 500, va lam mat ket qua cua entry SAU trong cung batch.
    /// Gio phai la per-entry rejection: entry loi bi tu choi, entry hop le NGAY SAU van duoc
    /// ap dung binh thuong, request van 200 OK.
    /// </summary>
    [Fact]
    public async Task Trung_TagId_trong_cung_1_entry_bi_tu_choi_rieng_khong_lam_hong_batch()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var badPromptId = Guid.NewGuid();
        var goodPromptId = Guid.NewGuid();
        var duplicateTagId = Guid.NewGuid();

        var response = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new object[]
            {
                InsertEntry(badPromptId, "Bad", "Bad content", tags: new[]
                {
                    new { tagId = duplicateTagId, name = "tag-a" },
                    new { tagId = duplicateTagId, name = "tag-b" }
                }),
                InsertEntry(goodPromptId, "Good", "Good content")
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("rejected", body!.Data!.Results[0].Status);
        Assert.Equal("applied", body.Data!.Results[1].Status);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        Assert.False(await db.Prompts.AnyAsync(p => p.PromptID == badPromptId));
        Assert.True(await db.Prompts.AnyAsync(p => p.PromptID == goodPromptId));
    }

    /// <summary>Cung finding 1, nhung cho Variable (VariableId trung trong cung 1 entry).</summary>
    [Fact]
    public async Task Trung_VariableId_trong_cung_1_entry_bi_tu_choi_rieng_khong_lam_hong_batch()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var badPromptId = Guid.NewGuid();
        var goodPromptId = Guid.NewGuid();
        var duplicateVariableId = Guid.NewGuid();

        var response = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new object[]
            {
                InsertEntry(badPromptId, "Bad", "Bad content", variables: new[]
                {
                    new { variableId = duplicateVariableId, varKey = "a" },
                    new { variableId = duplicateVariableId, varKey = "b" }
                }),
                InsertEntry(goodPromptId, "Good", "Good content")
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("rejected", body!.Data!.Results[0].Status);
        Assert.Equal("applied", body.Data!.Results[1].Status);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        Assert.False(await db.Prompts.AnyAsync(p => p.PromptID == badPromptId));
        Assert.True(await db.Prompts.AnyAsync(p => p.PromptID == goodPromptId));
    }

    /// <summary>
    /// Fix round 1, finding 4: omit ca CategoryId lan ClearCategory nghia la GIU NGUYEN category
    /// hien co (giong ngu nghia G12 cua Tags/Variables); chi ClearCategory=true tuong minh moi
    /// xoa han.
    /// </summary>
    [Fact]
    public async Task Bo_qua_categoryId_giu_nguyen_ClearCategory_thi_xoa()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { InsertEntry(promptId, "T1", "C1", categoryId: categoryId, categoryName: "Giu lai") }
        });

        // Update KHONG gui categoryId/clearCategory -> phai GIU NGUYEN category hien co.
        var responseOmit = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { new { promptId, operation = "update", baseVersion = 1, payload = new { title = "T2", content = "C2" } } }
        });
        Assert.Equal("applied", (await responseOmit.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>())!.Data!.Results[0].Status);

        var detailAfterOmit = await user.Client.GetFromJsonAsync<OperationResultOf<PromptDetailResponse>>($"/prompts/{promptId}?spaceUuid={spaceUuid}");
        Assert.Equal(categoryId, detailAfterOmit!.Data!.CategoryId);

        // Update gui clearCategory=true -> phai xoa han.
        var responseClear = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { new { promptId, operation = "update", baseVersion = 2, payload = new { title = "T3", content = "C3", clearCategory = true } } }
        });
        Assert.Equal("applied", (await responseClear.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>())!.Data!.Results[0].Status);

        var detailAfterClear = await user.Client.GetFromJsonAsync<OperationResultOf<PromptDetailResponse>>($"/prompts/{promptId}?spaceUuid={spaceUuid}");
        Assert.Null(detailAfterClear!.Data!.CategoryId);
    }

    /// <summary>
    /// Follow-up (description omit=unchanged, TDD cho bug mat du lieu that su): cung ngu nghia
    /// da ap dung cho CategoryId/ClearCategory o test ngay tren -- omit Description phai GIU
    /// NGUYEN gia tri hien co (bug cu: Expo client chua co cot description nen push update tu no
    /// se am tham xoa description da co tu client khac), ClearDescription=true tuong minh moi
    /// xoa han, va gui gia tri moi thi ghi de binh thuong.
    /// </summary>
    [Fact]
    public async Task Bo_qua_description_giu_nguyen_ClearDescription_thi_xoa()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();

        await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { new { promptId, operation = "insert", baseVersion = 0, payload = new { title = "T1", content = "C1", description = "Mo ta goc" } } }
        });

        // Update KHONG gui description (vd tu client chua co cot nay) -> phai GIU NGUYEN.
        var responseOmit = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { new { promptId, operation = "update", baseVersion = 1, payload = new { title = "T2", content = "C2" } } }
        });
        Assert.Equal("applied", (await responseOmit.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>())!.Data!.Results[0].Status);

        var detailAfterOmit = await user.Client.GetFromJsonAsync<OperationResultOf<PromptDetailResponse>>($"/prompts/{promptId}?spaceUuid={spaceUuid}");
        Assert.Equal("Mo ta goc", detailAfterOmit!.Data!.Description);

        // Update gui description moi -> ghi de binh thuong.
        var responseSet = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { new { promptId, operation = "update", baseVersion = 2, payload = new { title = "T3", content = "C3", description = "Mo ta moi" } } }
        });
        Assert.Equal("applied", (await responseSet.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>())!.Data!.Results[0].Status);

        var detailAfterSet = await user.Client.GetFromJsonAsync<OperationResultOf<PromptDetailResponse>>($"/prompts/{promptId}?spaceUuid={spaceUuid}");
        Assert.Equal("Mo ta moi", detailAfterSet!.Data!.Description);

        // Update gui clearDescription=true -> phai xoa han.
        var responseClear = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { new { promptId, operation = "update", baseVersion = 3, payload = new { title = "T4", content = "C4", clearDescription = true } } }
        });
        Assert.Equal("applied", (await responseClear.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>())!.Data!.Results[0].Status);

        var detailAfterClear = await user.Client.GetFromJsonAsync<OperationResultOf<PromptDetailResponse>>($"/prompts/{promptId}?spaceUuid={spaceUuid}");
        Assert.Null(detailAfterClear!.Data!.Description);
    }

    // --- Finding 1 (final review, USER DECISION MADE): chi tac gia HOAC CanManage (Owner/Admin)
    // moi duoc sua/xoa prompt cua NGUOI KHAC trong cung space. Dung Team space (co nhieu vai tro
    // thuc su) thay vi Personal (chi co 1 thanh vien, luon CanManage=true) de bai qua duoc kiem
    // tra nay. ---

    private async Task<Guid> CreateTeamSpaceAsync(params (Guid userId, SpaceMemberRole role)[] members)
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var space = new Space { SpaceType = SpaceType.Team, Name = "Team Test", OwnerUserID = members[0].userId };
        db.Spaces.Add(space);
        foreach (var (userId, role) in members)
            db.SpaceMembers.Add(new SpaceMember { SpaceID = space.SpaceID, UserID = userId, MemberRole = role });
        await db.SaveChangesAsync();
        return space.SpaceUUID;
    }

    /// <summary>
    /// Finding 1: thanh vien thuong (Member, khong phai tac gia) push-update len bai cua thanh
    /// vien KHAC trong cung Team space phai bi rejected -- khong duoc am tham ghi de noi dung
    /// nguoi khac chi vi cung la thanh vien cua space.
    /// </summary>
    [Fact]
    public async Task Thanh_vien_thuong_khong_duoc_sua_bai_cua_nguoi_khac_trong_Team_space()
    {
        var author = await TestUser.CreateAsync(_fixture);
        var otherMember = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await CreateTeamSpaceAsync(
            (author.UserId, SpaceMemberRole.Member),
            (otherMember.UserId, SpaceMemberRole.Member));
        var promptId = Guid.NewGuid();

        await author.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "Bai cua tac gia", "Noi dung goc") } });

        var response = await otherMember.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { UpdateEntry(promptId, 1, "Chiem doat", "Noi dung chiem doat") }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("rejected", body!.Data!.Results[0].Status);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.Equal("Bai cua tac gia", live.Title);
    }

    /// <summary>Finding 1: tac gia van sua duoc BAI CUA CHINH MINH du chi la Member (khong
    /// CanManage) trong Team space -- gioi han chi ap dung cho noi dung cua NGUOI KHAC.</summary>
    [Fact]
    public async Task Tac_gia_van_sua_duoc_bai_cua_chinh_minh_trong_Team_space()
    {
        var author = await TestUser.CreateAsync(_fixture);
        var otherMember = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await CreateTeamSpaceAsync(
            (author.UserId, SpaceMemberRole.Member),
            (otherMember.UserId, SpaceMemberRole.Member));
        var promptId = Guid.NewGuid();

        await author.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "V1", "C1") } });

        var response = await author.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { UpdateEntry(promptId, 1, "V2", "C2") }
        });

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("applied", body!.Data!.Results[0].Status);
        Assert.Equal(2, body.Data!.Results[0].NewVersion);
    }

    /// <summary>Finding 1: Owner/Admin (CanManage) duoc sua VA xoa bai cua NGUOI KHAC trong Team
    /// space -- day la ngoai le duy nhat cua gioi han moi.</summary>
    [Fact]
    public async Task Admin_sua_va_xoa_duoc_bai_cua_nguoi_khac_trong_Team_space()
    {
        var author = await TestUser.CreateAsync(_fixture);
        var admin = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await CreateTeamSpaceAsync(
            (author.UserId, SpaceMemberRole.Member),
            (admin.UserId, SpaceMemberRole.Admin));
        var promptId = Guid.NewGuid();

        await author.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "V1", "C1") } });

        var updateResponse = await admin.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { UpdateEntry(promptId, 1, "V2 boi admin", "C2 boi admin") }
        });
        var updateBody = await updateResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("applied", updateBody!.Data!.Results[0].Status);

        var deleteResponse = await admin.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { new { promptId, operation = "delete", baseVersion = 2, payload = (object?)null } }
        });
        var deleteBody = await deleteResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("applied", deleteBody!.Data!.Results[0].Status);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.True(live.IsDeleted);
    }

    // --- Finding 2 (final review, "P11 second half"): update roi vao dung mot dong DA bi
    // soft-delete phai la conflict, khong duoc am tham "hoi sinh" noi dung cu. ---

    /// <summary>
    /// baseVersion dung CHINH XAC bang Version hien tai (SAU khi da xoa) -- day la khe ho THAT
    /// su: mot kiem tra Version don thuan se thay "khop" va khong phat hien duoc gi sai neu
    /// khong co kiem tra IsDeleted rieng. Ky vong: conflict (khong phai applied), va Remote DTO
    /// phai bao IsDeleted=true de client biet ben kia da bi xoa.
    /// </summary>
    [Fact]
    public async Task Update_roi_vao_dong_da_soft_delete_thi_tra_conflict_khong_hoi_sinh_am_tham()
    {
        var user = await TestUser.CreateAsync(_fixture);
        var spaceUuid = await GetPersonalSpaceUuidAsync(user);
        var promptId = Guid.NewGuid();

        await user.Client.PostAsJsonAsync("/sync/push", new { spaceUuid, entities = new[] { InsertEntry(promptId, "V1", "C1") } });
        var deleteResponse = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { new { promptId, operation = "delete", baseVersion = 1, payload = (object?)null } }
        });
        var deletedVersion = (await deleteResponse.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>())!.Data!.Results[0].NewVersion!.Value;

        var response = await user.Client.PostAsJsonAsync("/sync/push", new
        {
            spaceUuid,
            entities = new[] { UpdateEntry(promptId, deletedVersion, "Hoi sinh am tham", "Noi dung hoi sinh") }
        });

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<SyncPushBatchResponse>>();
        Assert.Equal("conflict", body!.Data!.Results[0].Status);
        Assert.True(body.Data!.Results[0].Remote!.IsDeleted);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var live = await db.Prompts.AsNoTracking().FirstAsync(p => p.PromptID == promptId);
        Assert.True(live.IsDeleted);
        Assert.NotEqual("Hoi sinh am tham", live.Title);
    }
}
