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

    /// <summary>
    /// G11 (SECURITY): denormalized tu Prompt.SpaceID ngay luc RecordConflictAsync tao dong
    /// nay. Task 4 (resolve) can biet xung dot nay thuoc space nao de goi ISpaceContext
    /// .ResolveAsync va tu choi nguoi khong phai thanh vien -- request resolve KHONG co truong
    /// SpaceUuid rieng (client khong the tu khai bao space, tranh gia mao), nen day la NGUON
    /// DUY NHAT de suy ra space can kiem tra quyen.
    /// </summary>
    public Guid SpaceID { get; set; }

    [MaxLength(30)]
    public required string EntityType { get; set; }

    public Guid EntityID { get; set; }

    /// <summary>
    /// G9: "insert" | "update" | "delete" -- THAO TAC cuc bo dang xung dot, khong chi noi dung
    /// cua no. Can thiet de resolve biet "keep_local" tren mot xung dot local-delete-vs-remote-
    /// edit phai THUC SU xoa dong (IsDeleted=true), khong duoc hieu nham thanh mot ban cap nhat
    /// noi dung roi co gang "merge" vao mot dong da xoa.
    /// </summary>
    [MaxLength(10)]
    public required string LocalOperation { get; set; }

    [Column(TypeName = "jsonb")]
    public required string LocalPayloadJson { get; set; }

    [Column(TypeName = "jsonb")]
    public required string RemotePayloadJson { get; set; }

    /// <summary>
    /// G9: trang thai IsDeleted cua dong REMOTE tai thoi diem xung dot duoc ghi nhan --
    /// RemotePayloadJson chi co Title/Content/Description/CategoryID (xem RecordConflictAsync),
    /// khong tu no biet duoc ben "remote" thuc chat co dang la mot ban ghi DA XOA hay khong.
    /// </summary>
    public bool RemoteIsDeleted { get; set; }

    public int LocalVersion { get; set; }
    public int RemoteVersion { get; set; }
    public bool Resolved { get; set; }

    [MaxLength(20)]
    public string? ResolutionStrategy { get; set; }

    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
