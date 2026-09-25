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

    [MaxLength(30)]
    public required string EntityType { get; set; }

    public Guid EntityID { get; set; }

    [Column(TypeName = "jsonb")]
    public required string LocalPayloadJson { get; set; }

    [Column(TypeName = "jsonb")]
    public required string RemotePayloadJson { get; set; }

    public int LocalVersion { get; set; }
    public int RemoteVersion { get; set; }
    public bool Resolved { get; set; }

    [MaxLength(20)]
    public string? ResolutionStrategy { get; set; }

    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
