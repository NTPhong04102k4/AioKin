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

    public int Version { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
