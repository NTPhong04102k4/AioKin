using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Sync;

/// <summary>
/// Duoc ghi boi trigger sync.fn_prompts_write_log (xem migration AddSyncEngine), khong phai
/// EF SaveChanges. Cot payload duoc anh xa tu PayloadJson qua [Column("payload")] — trigger
/// insert vao cot "payload" (khong phai "payload_json" ma convention snake_case se sinh ra
/// tu ten property), sai ten cot se lam moi INSERT cua trigger loi khong tim thay cot.
/// created_at co HasDefaultValueSql("now()") trong DbContext vi trigger khong tu dat gia tri
/// nay va cot khong co default nao khac — thieu no thi moi lan trigger chay se loi NOT NULL.
/// </summary>
[Table("sync_log", Schema = "sync")]
public class SyncLogEntry
{
    [Key]
    public long SyncLogID { get; set; }

    public Guid SpaceID { get; set; }

    [MaxLength(30)]
    public required string EntityType { get; set; }

    public Guid EntityID { get; set; }

    [MaxLength(10)]
    public required string Operation { get; set; }

    [Column("payload", TypeName = "jsonb")]
    public string? PayloadJson { get; set; }

    [MaxLength(100)]
    public string? OriginDeviceId { get; set; }

    /// <summary>
    /// Carry-forward Task 3: nguoi THAT SU tao ra dong nay — ghi kem OriginDeviceId de pull
    /// suppress echo dung tren CAP (user, device), khong chi device (xem Prompt.UpdatedByUserId).
    /// Trigger sync.fn_prompts_write_log gan gia tri nay tu promptvault.prompts.updated_by_user_id;
    /// AddTagVariableSyncLogEntry (SyncService) gan truc tiep tu membership.UserID.
    /// </summary>
    public Guid? OriginUserId { get; set; }

    public int Version { get; set; }

    /// <summary>
    /// Fix round 1, finding 3a: KHONG dat initializer C# (vd "= DateTime.UtcNow") o day. Truoc
    /// ban fix nay, dong do khien MOI insert qua EF (vd AddTagVariableSyncLogEntry, cac dong
    /// "tags_variables") gui thang gio App-clock trong cau INSERT, DE QUA HasDefaultValueSql
    /// ("now()") ben DbContext — trong khi cac dong do trigger DB ghi (insert/update/delete noi
    /// dung binh thuong) luon dung DB-clock (trigger khong tu dat created_at, xem ghi chu tren
    /// class). Ket qua la hai "nguon" CreatedAt lech gio (app-clock vs DB-clock) tuy loai dong —
    /// mot nguon skew that su cho SyncService.SafetyWindow (xem SyncService.cs), vi cursor an
    /// toan so sanh CreatedAt voi DateTime.UtcNow CUA APP. De trong (default(DateTime)) thi EF
    /// coi day la "chua duoc dat" va BO QUA cot nay khoi INSERT, de DB tu ap "now()" — CUNG mot
    /// dong ho cho MOI loai dong sync_log, khong con lech.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
