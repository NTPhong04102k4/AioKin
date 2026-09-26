namespace AioKin.Models.ViewModel.Vault;

/// <summary>
/// G8: resolve phai tra ve version MOI de client tu cap nhat base_version cuc bo ma khong can
/// mot round-trip pull rieng chi de biet gia tri nay.
/// </summary>
public class ResolveConflictResponse
{
    public Guid PromptId { get; set; }
    public int NewVersion { get; set; }

    /// <summary>G9: cho client biet ket qua cuoi cung co phai la mot delete hay khong (vi du
    /// resolution "keep_local" tren mot xung dot local-delete-vs-remote-edit).</summary>
    public bool IsDeleted { get; set; }
}
