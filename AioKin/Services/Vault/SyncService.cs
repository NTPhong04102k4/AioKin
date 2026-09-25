using AioKin.Data;
using AioKin.Data.Entities.Sync;
using AioKin.Data.Entities.Vault;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Vault;
using AioKin.Models.ViewModel.Vault;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Vault;

/// <summary>
/// Nua "push" cua sync engine (pull/resolve o Task 3-4). Xem
/// .superpowers/sdd/2026-09-25-promptvault-sync-engine/progress.md muc "Task 2" cho toan bo
/// ruling — cac ghi chu ben duoi chi nhac lai diem quan trong nhat cua tung ruling, khong lap
/// lai toan van.
/// </summary>
public class SyncService : ISyncService
{
    private readonly AioKinDbContext _db;
    private readonly ISpaceContext _spaceContext;

    public SyncService(AioKinDbContext db, ISpaceContext spaceContext)
    {
        _db = db;
        _spaceContext = spaceContext;
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

        return OperationResult.Ok(data: results);
    }

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
            UpdatedDeviceId = deviceId
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
        prompt.CategoryID = refs.CategoryId;
        // Carry-forward: MOI write do push gay ra deu phai gan deviceId cua CHINH phien goi.
        prompt.UpdatedDeviceId = deviceId;

        // Expo gap G12: field null = giu nguyen, [] tuong minh = xoa het — chi dung lai khi
        // client THAT SU gui truong nay.
        var tagsProvided = entry.Payload.Tags is not null;
        var variablesProvided = entry.Payload.Variables is not null;

        if (tagsProvided)
            ReplaceTags(prompt, refs.TagIds);
        if (variablesProvided)
            ReplaceVariables(prompt, entry.Payload.Variables!);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Race hiem: version doi giua luc doc va luc save trong CHINH request nay (2 push
            // gan nhu dong thoi). P9: nap lai o day PHAI la TRACKED (khong AsNoTracking) — mot
            // mutation tren entity untracked se khong bao gio duoc SaveChangesAsync ghi xuong.
            _db.ChangeTracker.Clear();
            var latest = await LoadTrackedPromptByIdAsync(prompt.PromptID, cancellationToken);
            return await RecordConflictAsync(latest!, entry, cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return Rejected(entry.PromptId, "Khong the ap dung thay doi nay.");
        }

        // P12: neu cot prompt (title/content/description/category) KHONG doi, trigger se khong
        // bump version/ghi sync_log — tu ghi 1 dong thu cong neu tag/variable co doi that, de
        // Task 3 (pull) con biet ma dong bo cho cac thiet bi khac.
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
                await WriteTagVariableSyncLogAsync(membership.SpaceID, prompt, deviceId, cancellationToken);
        }

        return new SyncPushResponse { PromptId = entry.PromptId, Status = "applied", NewVersion = prompt.Version };
    }

    private Task<Prompt?> LoadTrackedPromptByIdAsync(Guid promptId, CancellationToken cancellationToken)
        => _db.Prompts
            .Include(p => p.PromptTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.Variables)
            .FirstOrDefaultAsync(p => p.PromptID == promptId, cancellationToken);

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

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            var latest = await LoadTrackedPromptByIdAsync(prompt.PromptID, cancellationToken);
            return await RecordConflictAsync(latest!, entry, cancellationToken);
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
        // file nay deu nap qua LoadTrackedPromptAsync/LoadTrackedPromptByIdAsync, khong bao gio
        // AsNoTracking) — neu khong, dong SaveChangesAsync ben duoi se khong ghi gi ca.
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

        if (payload.CategoryId is { } wantedCategoryId)
        {
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
    /// P12: ghi thu cong 1 dong sync_log cho thay doi CHI o tag/variable — trigger DB
    /// (sync.fn_prompts_write_log) chi lang nghe cot cua BANG prompts, khong biet gi ve
    /// prompt_tags/prompt_variables nen se khong tu ghi truong hop nay. "kind":"tags_variables"
    /// la dau hieu de Task 3 (pull) phan biet voi payload noi dung prompt day du (to_jsonb cua
    /// trigger khong co truong "kind").
    /// </summary>
    private async Task WriteTagVariableSyncLogAsync(Guid spaceId, Prompt prompt, string? deviceId, CancellationToken cancellationToken)
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
            Version = prompt.Version
        });

        await _db.SaveChangesAsync(cancellationToken);
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
            foreach (var tag in payload.Tags)
            {
                if (tag.TagId == Guid.Empty || string.IsNullOrWhiteSpace(tag.Name) || tag.Name.Length > 50)
                    return "Tag khong hop le.";
            }
        }

        if (payload.Variables is not null)
        {
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
            }
        }

        return null;
    }
}
