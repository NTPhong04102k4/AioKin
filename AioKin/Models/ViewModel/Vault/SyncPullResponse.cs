namespace AioKin.Models.ViewModel.Vault;

/// <summary>
/// Ket qua GET /sync/pull. Xem SyncService.PullAsync cho toan bo logic (retention/volume
/// snapshot fallback, echo suppression, safety-window cursor) va
/// .superpowers/sdd/2026-09-25-promptvault-sync-engine/progress.md muc "Task 3" cho cac ruling.
/// </summary>
public class SyncPullResponse
{
    public bool IsSnapshot { get; set; }

    /// <summary>
    /// Chi co gia tri khi IsSnapshot = true. Day la NOI DUNG JSON DAY DU (mot DTO projection —
    /// SyncSnapshotDto — khong phai raw entity/storage path). Ruling Expo gap G4: client di dong
    /// khong co (va khong nen co) service key cua Supabase Storage, nen phan hoi PHAI tu chua du
    /// du lieu de ap dung ngay — SnapshotUrl/storage path se buoc client goi them Supabase voi
    /// credential no khong co. Snapshot van duoc day len IBlobStorageService (BuildSnapshotFallbackAsync)
    /// de luu vet/audit qua BackupSnapshot.StoragePath, nhung day khong phai kenh client dung.
    /// </summary>
    public string? SnapshotJson { get; set; }

    public List<SyncChangeItem> Changes { get; set; } = [];

    /// <summary>Cursor de goi lai lan pull ke tiep (truyen vao ?since=).</summary>
    public long ResumeCursor { get; set; }
}

public class SyncChangeItem
{
    public long SyncLogId { get; set; }

    /// <summary>Hien tai luon la "prompt" — Category/Tag/PromptVariable dong bo nhu mot phan
    /// cua payload Prompt, khong co entity_type rieng (xem PromptTag.cs).</summary>
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    /// <summary>"insert" | "update" | "delete" — nguyen tu sync_log.operation. Luu y: mot
    /// soft-delete (PushDeleteAsync) la MOT UPDATE ve mat trigger (IsDeleted la mot cot, khong
    /// phai row DELETE that), nen no xuat hien o day voi Operation="update" va
    /// Prompt.IsDeleted=true — chi mot row DELETE that (dong da bi xoa cung, chua co duong ghi
    /// nao trong app hien tai lam viec nay) moi mang Operation="delete".</summary>
    public string Operation { get; set; } = string.Empty;

    public int Version { get; set; }

    /// <summary>
    /// True khi dong nay la mot delta CHI tag/variable (P12 — trigger DB khong tu ghi vi khong
    /// cot nao cua bang prompts doi, SyncService tu ghi dong nay). Client dung co nay de biet
    /// CHI ap Tags/Variables cua Prompt ben duoi, KHONG duoc ghi de Title/Content/Description
    /// bang gia tri trong do (gia tri do la snapshot LIVE tai thoi diem pull, khong phai lich su
    /// chinh xac tai thoi diem dong nay duoc ghi — xem HydrateChangesAsync).
    /// </summary>
    public bool TagsVariablesOnly { get; set; }

    /// <summary>Null khi Operation == "delete" (khong con gi de hien thi).</summary>
    public SyncPromptChangePayload? Prompt { get; set; }
}

/// <summary>
/// DTO projection rieng cho pull — KHONG phai raw Prompt entity (P5) va KHONG chua bat ky id
/// noi bo nao (SpaceID, AuthorUserID, UpdatedDeviceId...) — chi Title/Content/... va CategoryId
/// (Category dung Guid client-sinh nen an toan dua ra). Title/Content/Description/CategoryId/
/// IsDeleted lay tu chinh payload trigger ghi lai (snapshot dung luc do); Tags/Variables luon
/// hydrate tu bang song vi trigger tren promptvault.prompts khong biet gi ve prompt_tags/prompt_variables
/// (carry-forward Task 2).
/// </summary>
public class SyncPromptChangePayload
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public bool IsDeleted { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<PromptVariableResponse> Variables { get; set; } = [];
}

/// <summary>Mot prompt trong SnapshotJson — cung ly do P5 voi SyncPromptChangePayload.</summary>
public class SyncSnapshotPromptDto
{
    public Guid PromptId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public int Version { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<PromptVariableResponse> Variables { get; set; } = [];
}

public class SyncSnapshotDto
{
    public Guid SpaceUuid { get; set; }
    public DateTime GeneratedAt { get; set; }
    public List<SyncSnapshotPromptDto> Prompts { get; set; } = [];
}
