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

    /// <summary>
    /// Carry-forward Task 3 (progress.md): nguoi (khong chi thiet bi) THAT SU tao ra ban ghi
    /// sync_log gan nhat cho dong nay — origin_device_id mot minh la khong du de pull suppress
    /// echo an toan, vi DeviceInfo la chuoi client tu chon (2 THANH VIEN KHAC NHAU trong cung
    /// mot space chia se co the vo tinh/co y trung device_id). SyncService gan cot nay o CUNG
    /// cho voi UpdatedDeviceId (PushInsertAsync/ApplyUpdateOrConflictAsync/PushDeleteAsync), va
    /// trigger sync.fn_prompts_write_log lay no vao sync_log.origin_user_id (xem migration
    /// AddSyncLogOriginUser). Suppress dung tren CAP (origin_user_id, origin_device_id).
    /// </summary>
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>
    /// Follow-up (tag/variable-only versioning gap): hash SHA-256 hex (64 ky tu, lowercase) cua
    /// tap tag id (sorted) + chu ky variable (sorted) hien tai cua prompt nay — CUNG mot co so
    /// so sanh voi SyncService.VariableSignature/tagIdsBefore.SetEquals dang dung de phat hien
    /// P12 (tag/variable-only), khong phat minh tieu chi rieng. SyncService (PushInsertAsync/
    /// ApplyUpdateOrConflictAsync/ApplyResolvedPayloadAsync) PHAI gan lai cot nay MOI LAN tag/
    /// variable co the da doi, de no LUON phan anh dung trang thai hien tai. Trigger DB
    /// vault.fn_prompts_before_update so sanh OLD.meta_sig IS DISTINCT FROM NEW.meta_sig trong
    /// WHEN clause — nho vay mot thay doi CHI o tag/variable (truoc day trigger bo qua hoan
    /// toan, khong bump Version, khong ghi sync_log) gio bump Version + ghi sync_log giong het
    /// mot thay doi noi dung that su, dong nghia voi P22 (kiem tra version luc resolve) va
    /// baseVersion check luc push khong con bi "mu" truoc loai thay doi nay nua. Null cho cac
    /// dong cu (truoc migration nay) — trigger van an toan vi OLD/NEW deu null cho toi lan
    /// UPDATE dau tien co gan cot nay.
    /// </summary>
    [MaxLength(64)]
    public string? MetaSig { get; set; }

    public ICollection<PromptTag> PromptTags { get; set; } = [];
    public ICollection<PromptVariable> Variables { get; set; } = [];
}
