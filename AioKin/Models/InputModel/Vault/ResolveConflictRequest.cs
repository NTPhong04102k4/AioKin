using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Vault;

/// <summary>
/// POST /sync/conflicts/{id}/resolve. KHONG co truong SpaceUuid/DeviceId: space can kiem tra
/// quyen duoc suy ra TU CHINH ban ghi SyncConflict (SpaceID da duoc RecordConflictAsync luu lai
/// luc tao xung dot -- G11), khong tin bat ky gia tri nao client tu gui de tranh gia mao space.
/// DeviceId ghi vao Prompt.UpdatedDeviceId cung lay tu session cua caller, giong P16/Push/Pull.
/// </summary>
public class ResolveConflictRequest
{
    /// <summary>"keep_local" | "keep_remote" | "merged".</summary>
    [Required]
    public required string Resolution { get; set; }

    /// <summary>Bat buoc khi Resolution = "merged".</summary>
    public PromptPayload? MergedPayload { get; set; }
}
