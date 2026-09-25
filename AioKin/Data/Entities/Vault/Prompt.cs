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

    public ICollection<PromptTag> PromptTags { get; set; } = [];
    public ICollection<PromptVariable> Variables { get; set; } = [];
}
