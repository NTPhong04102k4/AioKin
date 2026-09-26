using System.Text.Json;
using AioKin.Data;
using AioKin.Data.Entities.Sync;
using AioKin.Data.Entities.Vault;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Vault;
using AioKin.Models.ViewModel.Vault;
using AioKin.Services.Common.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AioKin.Services.Vault;

/// <summary>
/// Push (Task 2) + Pull (Task 3) cua sync engine (resolve o Task 4). Xem
/// .superpowers/sdd/2026-09-25-promptvault-sync-engine/progress.md muc "Task 2"/"Task 3" cho
/// toan bo ruling — cac ghi chu ben duoi chi nhac lai diem quan trong nhat cua tung ruling,
/// khong lap lai toan van.
/// </summary>
public class SyncService : ISyncService
{
    private readonly AioKinDbContext _db;
    private readonly ISpaceContext _spaceContext;
    private readonly IBlobStorageService? _blobStorage;
    private readonly ILogger<SyncService> _logger;

    /// <summary>Neu so dong pending vuot nguong nay, snapshot re hon incremental.</summary>
    private const int RowCountThreshold = 500;

    /// <summary>
    /// Ruling Task 3 (cursor khong duoc bo qua mot commit gan-dong-thoi): sync_log_id la cot
    /// IDENTITY, gia tri duoc CAP luc INSERT nhung thu tu COMMIT giua 2 transaction gan nhu dong
    /// thoi khong dam bao khop voi thu tu id — mot transaction lay id THAP HON co the commit
    /// (va tro nen "visible") SAU mot transaction lay id CAO HON. Neu pull tra ve + tang cursor
    /// toi id cao ngay khi no vua commit, dong id thap con dang "in-flight" se vinh vien bi bo
    /// qua sau khi no cuoi cung cung commit (cursor da vuot qua no). Fix: chi tra ve (va chi cho
    /// cursor tien toi) nhung dong da "du gia" hon SafetyWindow — du thoi gian de bat ky
    /// transaction nao khac dang ghi gan do chac chan da commit xong.
    ///
    /// Fix round 1, finding 3: day la mot giam nhe XAC SUAT (probabilistic mitigation), KHONG
    /// PHAI mot dam bao toan hoc — no dua tren gia dinh "khong transaction nao ghi vao sync_log
    /// keo dai qua SafetyWindow", dieu ma khong co gi ep buoc ve mat co so du lieu (mot query cham
    /// bat thuong, GC pause, hay lock contention van co the vuot qua no trong ly thuyet). Fix dung
    /// dan (ra ngoai pham vi con lai cua plan nay, de lai lam viec sau): cursor dua tren tinh
    /// KHA KIEN cua transaction — vd so sanh voi pg_current_xact_id()/pg_snapshot_xmin() thay vi
    /// mot khoang thoi gian co dinh. Gia tri nay CAU HINH duoc qua Sync:PullSafetyWindowSeconds
    /// (mac dinh 10s, tang tu 2s ban dau — du sinh hon, giam xac suat that bai trong thuc te).
    /// </summary>
    private readonly TimeSpan _safetyWindow;

    public SyncService(AioKinDbContext db, ISpaceContext spaceContext, ILogger<SyncService> logger, IConfiguration configuration, IBlobStorageService? blobStorage = null)
    {
        _db = db;
        _spaceContext = spaceContext;
        _logger = logger;
        _blobStorage = blobStorage;
        // Fix round 1, finding 3b: cung mot "convention" voi JwtConfiguration.ResolveAccessTokenMinutes
        // — int.TryParse tren config[] thay vi GetValue<T>(), khong doi them goi Binder.
        _safetyWindow = TimeSpan.FromSeconds(
            int.TryParse(configuration["Sync:PullSafetyWindowSeconds"], out var seconds) && seconds > 0 ? seconds : 10);
    }

    public async Task<OperationResult> PushAsync(SyncPushRequest request, string? callerDeviceId, CancellationToken cancellationToken = default)
    {
        // P17 (SECURITY): moi thao tac push deu phai qua ISpaceContext truoc — bat ky thanh
        // vien nao cua space deu duoc push (khong can CanManage, giong sua noi dung chia se
        // binh thuong), nguoi ngoai space bi tu choi thang o day.
        var membership = await _spaceContext.ResolveAsync(request.SpaceUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("Forbidden", "Ban khong thuoc space nay.");

        await TouchDeviceAsync(membership.UserID, callerDeviceId, cancellationToken);

        var results = new List<SyncPushResponse>(request.Entities.Count);
        foreach (var entry in request.Entities)
            results.Add(await PushOneSafeAsync(membership, callerDeviceId, entry, cancellationToken));

        // Fix round 1, finding 5: tong ket cap-batch, de client chi doc OperationResult.Success
        // (luon true/200 o day, ke ca khi mot vai entry rieng le "rejected"/"conflict") khong
        // the bo lot that bai cua tung entry ben trong Results.
        var batch = new SyncPushBatchResponse
        {
            Results = results,
            AppliedCount = results.Count(r => r.Status == "applied"),
            ConflictCount = results.Count(r => r.Status == "conflict"),
            RejectedCount = results.Count(r => r.Status == "rejected")
        };

        return OperationResult.Ok(data: batch);
    }

    public async Task<OperationResult> PullAsync(Guid spaceUuid, long since, string? callerDeviceId, CancellationToken cancellationToken = default)
    {
        var membership = await _spaceContext.ResolveAsync(spaceUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("Forbidden", "Ban khong thuoc space nay.");

        // Fix round 1, finding 1 (P13 violation): since == 0 (client CHUA TUNG dong bo) LUON
        // duoc phuc vu bang SNAPSHOT — KHONG BAO GIO bang incremental, bat ke retention co "ve
        // nhu" con nguyen hay khong. Ly do: mot incremental "tu dau" chi dung neu TOAN BO lich su
        // sync_log ke tu dong dau tien cua space nay con nguyen ven — dieu nay KHONG THE kiem
        // chung duoc, vi sync_log_id la mot IDENTITY DUNG CHUNG toan he thong (khong rieng tung
        // space): "dong cu nhat con lai cho space nay co id lon" khong phan biet duoc giua "space
        // nay chi moi hoat dong gan day" (an toan) voi "mot phan lich su CUA CHINH space nay da bi
        // cron xoa" (khong an toan). Ban fix truoc day chi loai tru since=0 khoi retention check
        // (tranh false-positive) NHUNG van cho no di qua incremental — dung la sai chieu nguoc
        // lai: mot thiet bi moi pull LAN DAU sau khi lich su da bi xoa bot se nhan mot ket qua
        // incremental THIEU (Changes rong/thieu) voi IsSnapshot=false + cursor hop le, trong y het
        // "da dong bo day du" trong khi khong phai. Snapshot luon la mot "toan bo trang thai hien
        // tai" chinh xac cho MOI truong hop nay, ke ca khi space chua co gi (Prompts rong).
        if (since == 0)
            return await BuildSnapshotFallbackAsync(membership, cancellationToken);

        // Tu day tro di, since > 0 (client dang TIEP TUC mot phien dong bo THAT). Ruling P13: mot
        // cursor nho hon dong sync_log CU NHAT con lai cho SPACE NAY nghia la mot doan lich su co
        // the da bi cron retention xoa mat — khong the tra incremental an toan.
        //
        // Deferred (KHONG sua trong lan fix nay — xem task-3-report.md): cong thuc nay van co the
        // false-positive khi id toan he thong tang nhanh vi cac space KHAC, du CHINH space nay
        // chua tung mat gi — chap nhan duoc (an toan hon incremental sai), da duoc ledger o
        // progress.md la mot gap con lai.
        var oldestLogId = await _db.SyncLog
            .Where(s => s.SpaceID == membership.SpaceID)
            .OrderBy(s => s.SyncLogID)
            .Select(s => (long?)s.SyncLogID)
            .FirstOrDefaultAsync(cancellationToken);

        var retentionExceeded = oldestLogId is null || since < oldestLogId.Value - 1;

        if (retentionExceeded)
            return await BuildSnapshotFallbackAsync(membership, cancellationToken);

        // Volume check (toi uu, khong phai dung/sai): so dong se phai tra qua nhieu thi snapshot
        // re hon. Fix round 1, finding 4: BuildSnapshotFallbackAsync khong con doi hoi blob
        // storage phai san sang (upload la best-effort ben trong no) nen o day KHONG con dieu
        // kien "&& _blobStorage is not null" nhu truoc — thieu blob storage khong con la ly do de
        // BO QUA toi uu nay.
        var pendingCount = await _db.SyncLog.CountAsync(s => s.SpaceID == membership.SpaceID && s.SyncLogID > since, cancellationToken);
        if (pendingCount > RowCountThreshold)
            return await BuildSnapshotFallbackAsync(membership, cancellationToken);

        // Ruling Task 3 (an toan cursor cho commit gan-dong-thoi, XAC SUAT chu khong dam bao —
        // xem ghi chu tren field _safetyWindow o dau class): chi lay nhung dong da "du gia" hon
        // _safetyWindow.
        var threshold = DateTime.UtcNow - _safetyWindow;
        var rows = await _db.SyncLog
            .AsNoTracking()
            .Where(s => s.SpaceID == membership.SpaceID && s.SyncLogID > since && s.CreatedAt <= threshold)
            .OrderBy(s => s.SyncLogID)
            .ToListAsync(cancellationToken);

        // Cursor tien toi dong CUOI CUNG trong cua so an toan — KE CA nhung dong se bi loc
        // (echo cua chinh caller) o buoc sau, de lan pull ke tiep khong phai xin lai chinh thay
        // doi cua no moi lan.
        var resumeCursor = rows.Count > 0 ? rows[^1].SyncLogID : since;

        // Carry-forward Task 2 (echo suppression dung tren CAP user+device — xem
        // Prompt.UpdatedByUserId/SyncLogEntry.OriginUserId): khong tra lai cho CHINH (user,
        // device) vua tao ra thay doi do, no da biet no vua ghi gi. So sanh device MOT MINH la
        // spoofable — 2 thanh vien KHAC NHAU trong mot space chia se co the tu chon trung
        // device_id (DeviceInfo la chuoi client tu dat luc dang nhap).
        //
        // Fix round 1, finding 5: CHI suppress khi callerDeviceId THAT SU khac null. Khong co
        // dieu kien nay, hai phien KHONG co device (deviceId=null — vd web chua gui DeviceInfo)
        // CUA CUNG MOT USER se so khop null==null va an lan nhau MOT CACH SAI — device=null nghia
        // la "khong biet thiet bi nao", khong phai "mot thiet bi cu the ten null" nen khong the
        // dung de suy ra "day la CHINH phien vua ghi".
        var visibleRows = rows
            .Where(r => !(callerDeviceId is not null && r.OriginUserId == membership.UserID && r.OriginDeviceId == callerDeviceId))
            .ToList();

        var changes = await HydrateChangesAsync(membership.SpaceID, visibleRows, cancellationToken);

        return OperationResult.Ok(data: new SyncPullResponse
        {
            IsSnapshot = false,
            Changes = changes,
            ResumeCursor = resumeCursor
        });
    }

    /// <summary>
    /// Ruling P5 (blocker): serialize mot DTO projection TUONG MINH — KHONG serialize thang cac
    /// Prompt entity (do co navigation property hai chieu, vd Prompt.PromptTags[].Prompt tro
    /// nguoc lai chinh no — System.Text.Json nem System.Text.Json.JsonException tren graph vong
    /// nay). Ket qua duoc day len IBlobStorageService de luu vet/audit (BackupSnapshot) — BEST
    /// EFFORT (fix round 1, finding 4): upload/audit that bai KHONG duoc chan noi dung tra ve
    /// client, vi client KHONG doc snapshot qua duong Storage — no doc qua SnapshotJson tra thang
    /// trong response (Expo gap G4: client di dong khong co credential Supabase de tu tai storage
    /// path). Chi BackupSnapshot (audit trail noi bo) moi phu thuoc vao upload thanh cong.
    ///
    /// Fix round 1, finding 2: doc cursor (sync_log) TRUOC, prompts SAU, trong CUNG mot
    /// transaction REPEATABLE READ — Postgres chup lai MOT snapshot nhat quan tai thoi diem
    /// BeginTransaction cho ca hai lan doc. Neu doc rieng (khong transaction, nhu ban truoc), mot
    /// push xay ra GIUA hai lan doc chi lot vao MOT trong hai (cursor thay no nhung prompts thi
    /// khong, hoac nguoc lai) — client se resume qua mot thay doi no chua bao gio thuc su nhan
    /// duoc noi dung.
    /// </summary>
    private async Task<OperationResult> BuildSnapshotFallbackAsync(SpaceMembership membership, CancellationToken cancellationToken)
    {
        List<Prompt> prompts;
        long latestLogId;

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);

            // Cung ap dung _safetyWindow cho cursor cua snapshot (khong phai de tranh bo qua mot
            // commit gan-dong-thoi nhu nhanh incremental — snapshot da phan anh TOAN BO trang
            // thai hien tai roi — ma de ResumeCursor khong "vuot qua" mot dong vua ghi ma chinh
            // view REPEATABLE READ nay co the CHUA kip thay noi dung tuong ung).
            var threshold = DateTime.UtcNow - _safetyWindow;
            latestLogId = await _db.SyncLog
                .Where(s => s.SpaceID == membership.SpaceID && s.CreatedAt <= threshold)
                .OrderByDescending(s => s.SyncLogID)
                .Select(s => s.SyncLogID)
                .FirstOrDefaultAsync(cancellationToken);

            prompts = await _db.Prompts
                .AsNoTracking()
                .Include(p => p.Variables)
                .Include(p => p.PromptTags).ThenInclude(pt => pt.Tag)
                .Where(p => p.SpaceID == membership.SpaceID && !p.IsDeleted)
                .ToListAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Day la nhanh loi DUY NHAT con lai cua P13: that bai o day nghia la KHONG THE doc
            // duoc trang thai cua space (su co ha tang that su, vd mat ket noi DB) — khac han voi
            // upload len blob storage (best-effort, xem catch ben duoi), khong the "am tham tra
            // ve rong" vi khong co gi de tra ca.
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Khong the doc trang thai de tao snapshot cho space {SpaceUuid}", membership.SpaceUUID);
            return OperationResult.Fail("SyncUnavailable", "Khong the tao snapshot dong bo luc nay. Vui long thu lai sau.");
        }

        var snapshot = new SyncSnapshotDto
        {
            SpaceUuid = membership.SpaceUUID,
            GeneratedAt = DateTime.UtcNow,
            Prompts = [.. prompts.Select(p => new SyncSnapshotPromptDto
            {
                PromptId = p.PromptID,
                Title = p.Title,
                Content = p.Content,
                Description = p.Description,
                CategoryId = p.CategoryID,
                Version = p.Version,
                Tags = [.. p.PromptTags.Select(pt => pt.Tag?.Name ?? string.Empty)],
                Variables = [.. p.Variables.Select(v => new PromptVariableResponse { VarKey = v.VarKey, Label = v.Label, DefaultValue = v.DefaultValue })]
            })]
        };

        var json = JsonSerializer.Serialize(snapshot);

        // Fix round 1, finding 4: upload BEST-EFFORT. That bai (hoac IBlobStorageService chua
        // duoc dang ky) chi lam mat DI BackupSnapshot (audit trail) — KHONG lam mat noi dung tra
        // ve client, vi "json" da co san trong bo nho tu truoc do roi.
        if (_blobStorage is null)
        {
            _logger.LogWarning(
                "Snapshot cho space {SpaceUuid} khong duoc luu audit: IBlobStorageService chua duoc dang ky (thieu Storage:BaseUrl).",
                membership.SpaceUUID);
        }
        else
        {
            var path = $"snapshots/{membership.SpaceUUID}/{DateTime.UtcNow:yyyyMMddHHmmssfff}.json.gz";
            try
            {
                var url = await _blobStorage.UploadAsync(path, json, cancellationToken);

                _db.BackupSnapshots.Add(new BackupSnapshot
                {
                    SpaceID = membership.SpaceID,
                    TriggeredByUserID = membership.UserID,
                    SnapshotType = "sync_catchup",
                    StoragePath = url,
                    FileSizeBytes = json.Length,
                    PromptCount = prompts.Count
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _db.ChangeTracker.Clear();
                _logger.LogWarning(ex, "Upload snapshot audit that bai cho space {SpaceUuid} — van tra ve noi dung inline cho client.", membership.SpaceUUID);
            }
        }

        return OperationResult.Ok(data: new SyncPullResponse
        {
            IsSnapshot = true,
            SnapshotJson = json,
            ResumeCursor = latestLogId
        });
    }

    /// <summary>
    /// Carry-forward Task 2: trigger tren vault.prompts khong biet gi ve prompt_tags/
    /// prompt_variables (bang join rieng), nen payload no ghi vao sync_log KHONG BAO GIO co
    /// tags/variables — pull phai tu hydrate lai tu bang song. Ap dung cho CA dong
    /// tags_variables-only (P12, do SyncService tu ghi) LAN dong noi dung binh thuong (do
    /// trigger ghi) — ca hai deu thieu tags/variables trong PayloadJson.
    /// </summary>
    private async Task<List<SyncChangeItem>> HydrateChangesAsync(Guid spaceId, List<SyncLogEntry> rows, CancellationToken cancellationToken)
    {
        var promptIds = rows
            .Where(r => r.EntityType == "prompt" && r.Operation != "delete")
            .Select(r => r.EntityID)
            .Distinct()
            .ToList();

        var livePrompts = promptIds.Count == 0
            ? new Dictionary<Guid, Prompt>()
            : await _db.Prompts
                .AsNoTracking()
                .Include(p => p.PromptTags).ThenInclude(pt => pt.Tag)
                .Include(p => p.Variables)
                .Where(p => p.SpaceID == spaceId && promptIds.Contains(p.PromptID))
                .ToDictionaryAsync(p => p.PromptID, cancellationToken);

        var result = new List<SyncChangeItem>(rows.Count);

        foreach (var row in rows)
        {
            var item = new SyncChangeItem
            {
                SyncLogId = row.SyncLogID,
                EntityType = row.EntityType,
                EntityId = row.EntityID,
                Operation = row.Operation,
                Version = row.Version
            };

            if (row.EntityType == "prompt" && row.Operation != "delete")
            {
                livePrompts.TryGetValue(row.EntityID, out var live);
                var tags = live?.PromptTags.Select(pt => pt.Tag?.Name ?? string.Empty).ToList() ?? [];
                var variables = live?.Variables
                    .Select(v => new PromptVariableResponse { VarKey = v.VarKey, Label = v.Label, DefaultValue = v.DefaultValue })
                    .ToList() ?? [];

                JsonDocument? doc = null;
                try
                {
                    if (!string.IsNullOrEmpty(row.PayloadJson))
                        doc = JsonDocument.Parse(row.PayloadJson);
                }
                catch (JsonException)
                {
                    // Payload hong/khong doc duoc -> roi xuong nhanh live-fallback ben duoi thay
                    // vi lam bung ca pull.
                }

                var isTagsVariablesKind = doc is not null
                    && doc.RootElement.TryGetProperty("kind", out var kindEl)
                    && kindEl.GetString() == "tags_variables";

                item.TagsVariablesOnly = isTagsVariablesKind;

                if (doc is null || isTagsVariablesKind)
                {
                    // Dong tags_variables-only (P12) hoac thieu/hong payload: title/content/...
                    // lay tu BANG SONG (theo dinh nghia cua kind nay, cac truong do KHONG doi).
                    item.Prompt = new SyncPromptChangePayload
                    {
                        Title = live?.Title ?? string.Empty,
                        Content = live?.Content ?? string.Empty,
                        Description = live?.Description,
                        CategoryId = live?.CategoryID,
                        IsDeleted = live?.IsDeleted ?? false,
                        Tags = tags,
                        Variables = variables
                    };
                }
                else
                {
                    var root = doc.RootElement;
                    item.Prompt = new SyncPromptChangePayload
                    {
                        Title = TryGetString(root, "title") ?? string.Empty,
                        Content = TryGetString(root, "content") ?? string.Empty,
                        Description = TryGetString(root, "description"),
                        CategoryId = TryGetGuid(root, "category_id"),
                        IsDeleted = root.TryGetProperty("is_deleted", out var isDel) && isDel.ValueKind == JsonValueKind.True,
                        Tags = tags,
                        Variables = variables
                    };
                }

                doc?.Dispose();
            }

            result.Add(item);
        }

        return result;
    }

    private static string? TryGetString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var el) && el.ValueKind != JsonValueKind.Null ? el.GetString() : null;

    private static Guid? TryGetGuid(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var el) && el.ValueKind != JsonValueKind.Null ? el.GetGuid() : null;

    private async Task TouchDeviceAsync(Guid userId, string? deviceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return;

        // P16: khoa composite (UserID, DeviceID) — phai loc theo CA HAI, khong chi DeviceID,
        // neu khong 2 user co the "trung" mot device_id se ghi de dong cua nhau.
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.UserID == userId && d.DeviceID == deviceId, cancellationToken);
        if (device is null)
        {
            _db.Devices.Add(new Device { UserID = userId, DeviceID = deviceId, LastSyncedAt = DateTime.UtcNow });
        }
        else
        {
            device.LastSyncedAt = DateTime.UtcNow;
            device.IsStale = false;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Boc validate + dieu huong 1 entry. Loi o day KHONG duoc lam bung 500 hay huy ca batch —
    /// tra ve mot SyncPushResponse Status="rejected" cho rieng entry nay (ruling: per-entry
    /// rejection). Duy nhat mot lan SaveChangesAsync "chinh" cho phan ap dung that su moi entry
    /// (insert/update/delete) — do la don vi atomic tu nhien cua EF (mot SaveChanges = mot
    /// transaction ngam), nen khong can tu quan ly BeginTransaction/Savepoint rieng: that bai
    /// giua chung khong de lai ghi du dang, va ChangeTracker.Clear() sau loi dam bao rac cua
    /// entry hong khong lan sang entry ke tiep trong cung mot DbContext.
    /// </summary>
    private async Task<SyncPushResponse> PushOneSafeAsync(SpaceMembership membership, string? deviceId, PushPromptEntry entry, CancellationToken cancellationToken)
    {
        var validationError = Validate(entry);
        if (validationError is not null)
            return Rejected(entry.PromptId, validationError);

        try
        {
            return await DispatchAsync(membership, deviceId, entry, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fix round 1, finding 1c: luoi an toan CUOI CUNG. Mot so loi (vd EF nem
            // InvalidOperationException NGAY tai Add()/AddRange() khi 2 entity moi trong CUNG
            // 1 entry trung khoa chinh — xem finding 1a/1b) xay ra TRUOC khi toi
            // SaveChangesAsync, tuc la ngoai pham vi cac catch(DbUpdateException) hep hon o
            // ben duoi. Khong bat o day thi 1 entry hong se lam bung ca request (500) va keo
            // theo cac entry SAU trong CUNG batch bi mat ket qua/khong bao gio chay toi. Loai
            // tru OperationCanceledException de khong nuot mat viec huy request binh thuong.
            _db.ChangeTracker.Clear();
            return Rejected(entry.PromptId, "Khong the ap dung thay doi nay.");
        }
    }

    private async Task<SyncPushResponse> DispatchAsync(SpaceMembership membership, string? deviceId, PushPromptEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Operation == "delete")
            return await PushDeleteAsync(membership, deviceId, entry, cancellationToken);

        var existing = await LoadTrackedPromptAsync(membership.SpaceID, entry.PromptId, cancellationToken);

        if (existing is null)
        {
            // Carry-forward MUST (space_id trong conditional update/insert): mot PromptId trung
            // voi dong da ton tai o MOT SPACE KHAC phai bi tu choi CHUNG CHUNG (khong lo ton
            // tai), khong duoc am tham insert de vi pham khoa chinh roi rot xuong 500.
            var existsElsewhere = await _db.Prompts.AsNoTracking().AnyAsync(p => p.PromptID == entry.PromptId, cancellationToken);
            if (existsElsewhere)
                return Rejected(entry.PromptId, "Khong the ap dung thay doi nay.");

            return await PushInsertAsync(membership, deviceId, entry, cancellationToken);
        }

        // G10: mot "insert" ma dong da ton tai (client retry vi response lan truoc bi mat) phai
        // duoc kiem tra noi dung giong het truoc khi lam gi khac — khong duoc coi baseVersion=0
        // (thuong gui cho insert) khac Version hien tai la mot conflict moi.
        if (entry.Operation == "insert")
            return await PushRetriedInsertAsync(membership, deviceId, existing, entry, cancellationToken);

        return await ApplyUpdateOrConflictAsync(membership, deviceId, existing, entry, cancellationToken);
    }

    private Task<Prompt?> LoadTrackedPromptAsync(Guid spaceId, Guid promptId, CancellationToken cancellationToken)
        => _db.Prompts
            .Include(p => p.PromptTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.Variables)
            .FirstOrDefaultAsync(p => p.PromptID == promptId && p.SpaceID == spaceId, cancellationToken);

    private async Task<SyncPushResponse> PushInsertAsync(SpaceMembership membership, string? deviceId, PushPromptEntry entry, CancellationToken cancellationToken)
    {
        var (refs, rejectReason) = await ResolveCategoryAndTagsAsync(membership.SpaceID, entry.Payload!, cancellationToken);
        if (refs is null)
            return Rejected(entry.PromptId, rejectReason!);

        if (refs.NewCategory is not null)
            _db.Categories.Add(refs.NewCategory);
        if (refs.NewTags.Count > 0)
            _db.Tags.AddRange(refs.NewTags);

        var prompt = new Prompt
        {
            PromptID = entry.PromptId,
            SpaceID = membership.SpaceID,
            AuthorUserID = membership.UserID,
            CategoryID = refs.CategoryId,
            Title = entry.Payload!.Title,
            Content = entry.Payload.Content,
            Description = entry.Payload.Description,
            // Carry-forward: KHONG BAO GIO tin Version tu client tren insert — luon bat dau 1.
            Version = 1,
            UpdatedDeviceId = deviceId,
            // Carry-forward Task 3: nguon cho sync_log.origin_user_id qua trigger — xem
            // Prompt.UpdatedByUserId.
            UpdatedByUserId = membership.UserID
        };
        prompt.Variables = [.. (entry.Payload.Variables ?? []).Select(v => new PromptVariable
        {
            VariableID = v.VariableId, PromptID = prompt.PromptID, VarKey = v.VarKey, Label = v.Label, DefaultValue = v.DefaultValue, VarType = v.VarType
        })];
        prompt.PromptTags = [.. refs.TagIds.Select(id => new PromptTag { PromptID = prompt.PromptID, TagID = id })];

        _db.Prompts.Add(prompt);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return Rejected(entry.PromptId, "Khong the ap dung thay doi nay.");
        }

        return new SyncPushResponse { PromptId = entry.PromptId, Status = "applied", NewVersion = prompt.Version };
    }

    private async Task<SyncPushResponse> PushRetriedInsertAsync(SpaceMembership membership, string? deviceId, Prompt existing, PushPromptEntry entry, CancellationToken cancellationToken)
    {
        var (refs, rejectReason) = await ResolveCategoryAndTagsAsync(membership.SpaceID, entry.Payload!, cancellationToken);
        if (refs is null)
            return Rejected(entry.PromptId, rejectReason!);

        // G10: cung noi dung -> coi la thanh cong khong lam gi them, KHONG phai conflict moi.
        if (IsIdenticalRetry(existing, entry.Payload!, refs.CategoryId, refs.TagIds))
            return new SyncPushResponse { PromptId = entry.PromptId, Status = "applied", NewVersion = existing.Version };

        // Trung PromptId nhung noi dung khac -> day thuc chat la 1 sua doi dang xung dot voi
        // baseVersion client gui, di theo dung luong conflict/apply binh thuong.
        return await ApplyUpdateOrConflictAsync(membership, deviceId, existing, entry, cancellationToken);
    }

    private async Task<SyncPushResponse> ApplyUpdateOrConflictAsync(SpaceMembership membership, string? deviceId, Prompt prompt, PushPromptEntry entry, CancellationToken cancellationToken)
    {
        if (prompt.Version != entry.BaseVersion)
            return await RecordConflictAsync(prompt, entry, cancellationToken);

        var (refs, rejectReason) = await ResolveCategoryAndTagsAsync(membership.SpaceID, entry.Payload!, cancellationToken);
        if (refs is null)
            return Rejected(entry.PromptId, rejectReason!);

        // P12: chup lai TRUOC khi sua — can biet sau do day co phai la thay doi CHI tag/variable
        // hay khong, vi trigger DB chi bump version/ghi sync_log khi cot noi dung that su doi.
        var titleBefore = prompt.Title;
        var contentBefore = prompt.Content;
        var descriptionBefore = prompt.Description;
        var categoryBefore = prompt.CategoryID;
        var tagIdsBefore = prompt.PromptTags.Select(pt => pt.TagID).ToHashSet();
        var variablesBefore = prompt.Variables.Select(VariableSignature).OrderBy(s => s).ToArray();

        if (refs.NewCategory is not null)
            _db.Categories.Add(refs.NewCategory);
        if (refs.NewTags.Count > 0)
            _db.Tags.AddRange(refs.NewTags);

        prompt.Title = entry.Payload!.Title;
        prompt.Content = entry.Payload.Content;
        prompt.Description = entry.Payload.Description;
        // Fix round 1, finding 4: CHI ghi de CategoryID khi client THAT SU gui CategoryId hoac
        // ClearCategory=true (refs.CategoryProvided) — omit ca hai nghia la giu nguyen category
        // hien co, dung theo ngu nghia G12 da ap dung cho Tags/Variables (truoc day omit se bi
        // hieu nham thanh "xoa category", sai voi ruling).
        if (refs.CategoryProvided)
            prompt.CategoryID = refs.CategoryId;
        // Carry-forward: MOI write do push gay ra deu phai gan deviceId cua CHINH phien goi.
        prompt.UpdatedDeviceId = deviceId;
        // Carry-forward Task 3: cung ly do — nguon cho sync_log.origin_user_id qua trigger.
        prompt.UpdatedByUserId = membership.UserID;

        // Expo gap G12: field null = giu nguyen, [] tuong minh = xoa het — chi dung lai khi
        // client THAT SU gui truong nay.
        var tagsProvided = entry.Payload.Tags is not null;
        var variablesProvided = entry.Payload.Variables is not null;

        if (tagsProvided)
            ReplaceTags(prompt, refs.TagIds);
        if (variablesProvided)
            ReplaceVariables(prompt, entry.Payload.Variables!);

        // P12: neu cot prompt (title/content/description/category) KHONG doi, trigger se khong
        // bump version/ghi sync_log — tu ghi 1 dong thu cong neu tag/variable co doi that, de
        // Task 3 (pull) con biet ma dong bo cho cac thiet bi khac. Quyet dinh + Add() dong log
        // nay o DAY (TRUOC SaveChangesAsync — fix round 1, finding 2): du lieu can de so sanh
        // (title/content/description/category MOI, refs.TagIds, danh sach variable moi) da co
        // du trong bo nho, khong can doi SaveChangesAsync tra ve gi ca. Prompt.Version cung da
        // dung san o day cho truong hop nay: khi noi dung KHONG doi, trigger vault
        // .fn_prompts_before_update khong chay nen version se khong doi qua SaveChangesAsync —
        // gia tri hien tai cua prompt.Version chinh la gia tri cuoi cung. Gop chung vao MOT
        // SaveChangesAsync duy nhat voi prompt/tag/variable dam bao tag/variable va sync_log
        // hoac cung thanh cong hoac cung khong ghi gi — khong con truong hop tag da luu ma
        // sync_log bi mat vi mot SaveChangesAsync THU HAI rieng biet loi giua chung.
        var contentUnchanged =
            titleBefore == prompt.Title &&
            contentBefore == prompt.Content &&
            descriptionBefore == prompt.Description &&
            categoryBefore == prompt.CategoryID;

        if (contentUnchanged)
        {
            var tagsActuallyChanged = tagsProvided && !tagIdsBefore.SetEquals(refs.TagIds);
            var variablesActuallyChanged = variablesProvided &&
                !variablesBefore.SequenceEqual(prompt.Variables.Select(VariableSignature).OrderBy(s => s));

            if (tagsActuallyChanged || variablesActuallyChanged)
                AddTagVariableSyncLogEntry(membership.SpaceID, prompt, deviceId, membership.UserID);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Race hiem: version doi giua luc doc va luc save trong CHINH request nay (2 push
            // gan nhu dong thoi). P9: nap lai o day PHAI la TRACKED (khong AsNoTracking) — mot
            // mutation tren entity untracked se khong bao gio duoc SaveChangesAsync ghi xuong.
            // Fix round 1, finding 3: phai loc lai THEO SPACE (khong phai chi PromptID) va xu ly
            // truong hop dong da bien mat (vd space bi xoa cascade giua chung) bang Rejected
            // thay vi latest! (se nem NullReferenceException/500).
            _db.ChangeTracker.Clear();
            var latest = await LoadTrackedPromptAsync(membership.SpaceID, prompt.PromptID, cancellationToken);
            if (latest is null)
                return Rejected(entry.PromptId, "Khong the ap dung thay doi nay.");

            return await RecordConflictAsync(latest, entry, cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return Rejected(entry.PromptId, "Khong the ap dung thay doi nay.");
        }

        return new SyncPushResponse { PromptId = entry.PromptId, Status = "applied", NewVersion = prompt.Version };
    }

    private async Task<SyncPushResponse> PushDeleteAsync(SpaceMembership membership, string? deviceId, PushPromptEntry entry, CancellationToken cancellationToken)
    {
        var prompt = await LoadTrackedPromptAsync(membership.SpaceID, entry.PromptId, cancellationToken);
        if (prompt is null)
            // Da bi xoa/chua tung ton tai TRONG SPACE NAY — coi nhu thanh cong (idempotent),
            // khop y dinh cua client (muon no bien mat). Khong phan biet "chua co" voi "thuoc
            // space khac" de khong lo thong tin ton tai o noi khac.
            return new SyncPushResponse { PromptId = entry.PromptId, Status = "applied" };

        if (prompt.Version != entry.BaseVersion)
            return await RecordConflictAsync(prompt, entry, cancellationToken);

        prompt.IsDeleted = true;
        prompt.UpdatedDeviceId = deviceId;
        // Carry-forward Task 3: cung ly do — nguon cho sync_log.origin_user_id qua trigger.
        prompt.UpdatedByUserId = membership.UserID;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Fix round 1, finding 3: cung sua nhu ApplyUpdateOrConflictAsync — loc lai THEO
            // SPACE va tra Rejected neu dong da bien mat, khong dung latest! (NullReferenceException).
            _db.ChangeTracker.Clear();
            var latest = await LoadTrackedPromptAsync(membership.SpaceID, prompt.PromptID, cancellationToken);
            if (latest is null)
                return Rejected(entry.PromptId, "Khong the ap dung thay doi nay.");

            return await RecordConflictAsync(latest, entry, cancellationToken);
        }

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

        var conflict = new SyncConflict
        {
            EntityType = "prompt",
            EntityID = remote.PromptID,
            LocalPayloadJson = localPayload,
            RemotePayloadJson = remotePayload,
            LocalVersion = entry.BaseVersion,
            RemoteVersion = remote.Version
        };

        _db.SyncConflicts.Add(conflict);
        // P9: "remote" o day PHAI la mot entity TRACKED (moi ham goi RecordConflictAsync trong
        // file nay deu nap qua LoadTrackedPromptAsync, khong bao gio AsNoTracking) — neu khong,
        // dong SaveChangesAsync ben duoi se khong ghi gi ca.
        remote.HasConflict = true;

        await _db.SaveChangesAsync(cancellationToken);

        return new SyncPushResponse
        {
            PromptId = remote.PromptID,
            Status = "conflict",
            ConflictId = conflict.ConflictID,
            Remote = new PromptDetailResponse
            {
                PromptId = remote.PromptID,
                Title = remote.Title,
                Content = remote.Content,
                Description = remote.Description,
                CategoryId = remote.CategoryID,
                Version = remote.Version,
                HasConflict = true,
                Tags = [.. remote.PromptTags.Select(pt => pt.Tag?.Name ?? string.Empty)],
                Variables = [.. remote.Variables.Select(v => new PromptVariableResponse { VarKey = v.VarKey, Label = v.Label, DefaultValue = v.DefaultValue })]
            }
        };
    }

    private sealed class ResolvedRefs
    {
        /// <summary>
        /// Fix round 1, finding 4: true khi client THAT SU gui CategoryId khac null hoac
        /// ClearCategory=true — chi khi do noi goi moi duoc ghi de Prompt.CategoryID. False (ca
        /// CategoryId lan ClearCategory deu vang mat) nghia la "khong dong den", giu nguyen
        /// category hien co — giong het ngu nghia G12 da ap dung cho Tags/Variables.
        /// </summary>
        public bool CategoryProvided;
        public Guid? CategoryId;
        public Category? NewCategory;
        public List<Guid> TagIds = [];
        public List<Tag> NewTags = [];
    }

    /// <summary>
    /// CHI DOC — khong Add gi vao _db o day. Ly do: ham nay cung duoc dung boi nhanh G10
    /// (retry-insert) de kiem tra "giong het" TRUOC KHI biet co thuc su ghi gi khong; neu Add
    /// luon o day thi 1 request bi coi la no-op van co the de lai Category/Tag "mo coi" trong
    /// ChangeTracker roi bi SaveChangesAsync cua MOT ENTRY KHAC (cung batch) vo tinh flush ra.
    /// Category/Tag moi (neu co) duoc tra ve qua NewCategory/NewTags de noi goi tu quyet dinh
    /// co Add hay khong.
    /// </summary>
    private async Task<(ResolvedRefs? Refs, string? RejectReason)> ResolveCategoryAndTagsAsync(Guid spaceId, PromptPayload payload, CancellationToken cancellationToken)
    {
        var refs = new ResolvedRefs();

        if (payload.ClearCategory)
        {
            // Fix round 1, finding 4: xoa han category — tuong minh, khac voi "omit" (giu nguyen).
            refs.CategoryProvided = true;
            refs.CategoryId = null;
        }
        else if (payload.CategoryId is { } wantedCategoryId)
        {
            refs.CategoryProvided = true;
            var found = await _db.Categories.AsNoTracking()
                .Where(c => c.CategoryID == wantedCategoryId)
                .Select(c => new { c.SpaceID })
                .FirstOrDefaultAsync(cancellationToken);

            if (found is not null)
            {
                // Carry-forward MUST (category/tag same-space): ton tai nhung o SPACE KHAC ->
                // tu choi CHUNG CHUNG, khong lo la no co ton tai o noi khac.
                if (found.SpaceID != spaceId)
                    return (null, "Du lieu tham chieu khong hop le.");

                refs.CategoryId = wantedCategoryId;
            }
            else
            {
                // Chua ton tai theo Id — truoc khi tao moi, doi chieu (SpaceID, Name) voi unique
                // index hien co de KHONG tao trung ten trong cung mot space.
                var name = string.IsNullOrWhiteSpace(payload.CategoryName) ? "Chua dat ten" : payload.CategoryName!;
                var reuseId = await _db.Categories.AsNoTracking()
                    .Where(c => c.SpaceID == spaceId && c.Name == name)
                    .Select(c => (Guid?)c.CategoryID)
                    .FirstOrDefaultAsync(cancellationToken);

                if (reuseId is { } existingId)
                {
                    refs.CategoryId = existingId;
                }
                else
                {
                    refs.CategoryId = wantedCategoryId;
                    refs.NewCategory = new Category { CategoryID = wantedCategoryId, SpaceID = spaceId, Name = name };
                }
            }
        }

        if (payload.Tags is not null)
        {
            // Ten da xu ly TRONG CHINH request nay (chua kip SaveChanges nen DB chua thay) —
            // tranh tao 2 tag moi trung ten khi client gui 2 TagRef cung Name trong 1 payload.
            var pendingNames = new Dictionary<string, Guid>(StringComparer.Ordinal);

            foreach (var tagRef in payload.Tags)
            {
                var found = await _db.Tags.AsNoTracking()
                    .Where(t => t.TagID == tagRef.TagId)
                    .Select(t => new { t.SpaceID })
                    .FirstOrDefaultAsync(cancellationToken);

                if (found is not null)
                {
                    if (found.SpaceID != spaceId)
                        return (null, "Du lieu tham chieu khong hop le.");

                    refs.TagIds.Add(tagRef.TagId);
                    continue;
                }

                if (pendingNames.TryGetValue(tagRef.Name, out var pendingId))
                {
                    refs.TagIds.Add(pendingId);
                    continue;
                }

                var reuseId = await _db.Tags.AsNoTracking()
                    .Where(t => t.SpaceID == spaceId && t.Name == tagRef.Name)
                    .Select(t => (Guid?)t.TagID)
                    .FirstOrDefaultAsync(cancellationToken);

                if (reuseId is { } existingId)
                {
                    refs.TagIds.Add(existingId);
                    pendingNames[tagRef.Name] = existingId;
                }
                else
                {
                    refs.TagIds.Add(tagRef.TagId);
                    refs.NewTags.Add(new Tag { TagID = tagRef.TagId, SpaceID = spaceId, Name = tagRef.Name });
                    pendingNames[tagRef.Name] = tagRef.TagId;
                }
            }
        }

        // Fix round 1, finding 1a: du Validate() da chan trung TagId THO trong payload, van co
        // the co 2 TagId THO KHAC NHAU cung tro ve MOT id sau khi resolve-by-name (2 TagRef moi,
        // trung Name, chua ton tai trong DB — ca hai cung "gop" vao id cua cai duoc xu ly truoc,
        // xem pendingNames o tren). Khong dedupe o day thi buoc gan PromptTags ben ngoai se tao
        // 2 dong (PromptID, TagID) trung khoa chinh -> EF nem InvalidOperationException khi Add.
        refs.TagIds = [.. refs.TagIds.Distinct()];

        return (refs, null);
    }

    private static bool IsIdenticalRetry(Prompt existing, PromptPayload payload, Guid? resolvedCategoryId, List<Guid> resolvedTagIds)
    {
        if (!string.Equals(existing.Title, payload.Title, StringComparison.Ordinal)) return false;
        if (!string.Equals(existing.Content, payload.Content, StringComparison.Ordinal)) return false;
        if (!string.Equals(existing.Description, payload.Description, StringComparison.Ordinal)) return false;
        if (existing.CategoryID != resolvedCategoryId) return false;

        if (payload.Tags is not null)
        {
            var existingTagIds = existing.PromptTags.Select(pt => pt.TagID).ToHashSet();
            if (!existingTagIds.SetEquals(resolvedTagIds)) return false;
        }

        if (payload.Variables is not null)
        {
            var existingSig = existing.Variables.Select(VariableSignature).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            var incomingSig = payload.Variables
                .Select(v => VariableSignature(v.VariableId, v.VarKey, v.Label, v.DefaultValue, v.VarType))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            if (!existingSig.SequenceEqual(incomingSig, StringComparer.Ordinal)) return false;
        }

        return true;
    }

    private static string VariableSignature(PromptVariable v) => VariableSignature(v.VariableID, v.VarKey, v.Label, v.DefaultValue, v.VarType);

    private static string VariableSignature(Guid variableId, string varKey, string? label, string? defaultValue, string varType)
        => string.Join('|', variableId, varKey, label, defaultValue, varType);

    private void ReplaceTags(Prompt prompt, List<Guid> tagIds)
    {
        _db.PromptTags.RemoveRange(prompt.PromptTags);
        prompt.PromptTags.Clear();
        foreach (var id in tagIds)
            prompt.PromptTags.Add(new PromptTag { PromptID = prompt.PromptID, TagID = id });
    }

    private void ReplaceVariables(Prompt prompt, List<PromptVariablePayload> variables)
    {
        _db.PromptVariables.RemoveRange(prompt.Variables);
        prompt.Variables.Clear();
        foreach (var v in variables)
        {
            prompt.Variables.Add(new PromptVariable
            {
                VariableID = v.VariableId, PromptID = prompt.PromptID, VarKey = v.VarKey, Label = v.Label, DefaultValue = v.DefaultValue, VarType = v.VarType
            });
        }
    }

    /// <summary>
    /// P12: chuan bi 1 dong sync_log cho thay doi CHI o tag/variable — trigger DB
    /// (sync.fn_prompts_write_log) chi lang nghe cot cua BANG prompts, khong biet gi ve
    /// prompt_tags/prompt_variables nen se khong tu ghi truong hop nay. "kind":"tags_variables"
    /// la dau hieu de Task 3 (pull) phan biet voi payload noi dung prompt day du (to_jsonb cua
    /// trigger khong co truong "kind").
    ///
    /// Fix round 1, finding 2: ham nay CHI Add() vao ChangeTracker, KHONG tu SaveChangesAsync —
    /// goi tai ApplyUpdateOrConflictAsync TRUOC dong SaveChangesAsync "chinh" cua prompt/tag/
    /// variable, de ca 3 loai thay doi cung nam trong MOT giao dich atomic. Truoc day ham nay
    /// tu Save rieng SAU khi prompt/tag/variable da Save xong — neu lan Save thu hai nay loi thi
    /// tag da duoc luu nhung khong co sync_log tuong ung, thiet bi khac se khong bao gio thay
    /// thay doi do o lan pull ke tiep.
    /// </summary>
    private void AddTagVariableSyncLogEntry(Guid spaceId, Prompt prompt, string? deviceId, Guid userId)
    {
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            kind = "tags_variables",
            promptId = prompt.PromptID,
            tagIds = prompt.PromptTags.Select(pt => pt.TagID).ToArray(),
            variables = prompt.Variables.Select(v => new { v.VariableID, v.VarKey, v.Label, v.DefaultValue, v.VarType }).ToArray()
        });

        _db.SyncLog.Add(new SyncLogEntry
        {
            SpaceID = spaceId,
            EntityType = "prompt",
            EntityID = prompt.PromptID,
            Operation = "update",
            PayloadJson = payloadJson,
            OriginDeviceId = deviceId,
            // Carry-forward Task 3: dong nay duoc app tu ghi (khong qua trigger) nen gan
            // OriginUserId truc tiep tu membership.UserID cua chinh phien push.
            OriginUserId = userId,
            Version = prompt.Version
        });
    }

    private static SyncPushResponse Rejected(Guid promptId, string error) => new() { PromptId = promptId, Status = "rejected", Error = error };

    /// <summary>
    /// Validate THU CONG (khong dua vao DataAnnotations/model binding — loi o do se lam
    /// [ApiController] tu tra 400 cho CA REQUEST truoc khi vao toi action, pha vo yeu cau
    /// per-entry rejection). Tra ve null neu hop le, nguoc lai la thong bao chung chung.
    /// </summary>
    private static string? Validate(PushPromptEntry entry)
    {
        if (entry.PromptId == Guid.Empty)
            return "PromptId khong hop le.";

        if (entry.Operation is not ("insert" or "update" or "delete"))
            return "Operation khong hop le.";

        if (entry.Operation == "delete")
            return null;

        var payload = entry.Payload;
        if (payload is null)
            return "Thieu payload.";

        if (string.IsNullOrWhiteSpace(payload.Title) || payload.Title.Length > 200)
            return "Title khong hop le.";

        if (string.IsNullOrEmpty(payload.Content))
            return "Content khong hop le.";

        if (payload.Description is { Length: > 500 })
            return "Description qua dai.";

        if (payload.CategoryName is { Length: > 80 })
            return "CategoryName qua dai.";

        if (payload.Tags is not null)
        {
            var seenTagIds = new HashSet<Guid>();
            foreach (var tag in payload.Tags)
            {
                if (tag.TagId == Guid.Empty || string.IsNullOrWhiteSpace(tag.Name) || tag.Name.Length > 50)
                    return "Tag khong hop le.";

                // Fix round 1, finding 1b: TagId trung nhau TRONG CUNG mot payload phai bi tu
                // choi rieng entry nay o day, khong duoc de lot xuong EF roi crash luc Add().
                if (!seenTagIds.Add(tag.TagId))
                    return "Tag bi trung trong cung 1 entry.";
            }
        }

        if (payload.Variables is not null)
        {
            var seenVariableIds = new HashSet<Guid>();
            foreach (var v in payload.Variables)
            {
                if (v.VariableId == Guid.Empty || string.IsNullOrWhiteSpace(v.VarKey) || v.VarKey.Length > 50)
                    return "Variable khong hop le.";
                if (v.Label is { Length: > 100 })
                    return "Variable Label qua dai.";
                if (v.DefaultValue is { Length: > 500 })
                    return "Variable DefaultValue qua dai.";
                if (v.VarType is { Length: > 20 })
                    return "Variable VarType qua dai.";

                // Fix round 1, finding 1b: cung ly do voi Tag o tren.
                if (!seenVariableIds.Add(v.VariableId))
                    return "Variable bi trung trong cung 1 entry.";
            }
        }

        return null;
    }
}
