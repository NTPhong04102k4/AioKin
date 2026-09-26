using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Sync;

[Table("backup_snapshots", Schema = "sync")]
public class BackupSnapshot
{
    [Key]
    public Guid SnapshotID { get; set; } = Guid.NewGuid();

    public Guid SpaceID { get; set; }
    public Guid TriggeredByUserID { get; set; }

    /// <summary>'manual' | 'scheduled' | 'pre_sync' | 'sync_catchup'.</summary>
    [MaxLength(20)]
    public required string SnapshotType { get; set; }

    [MaxLength(500)]
    public required string StoragePath { get; set; }

    public long? FileSizeBytes { get; set; }
    public int? PromptCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
