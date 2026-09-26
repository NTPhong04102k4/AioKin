namespace AioKin.Models.ViewModel.Vault;

public class SyncPushResponse
{
    public Guid PromptId { get; set; }

    /// <summary>"applied" | "conflict" | "rejected".</summary>
    public string Status { get; set; } = string.Empty;

    public int? NewVersion { get; set; }

    /// <summary>Chi co gia tri khi Status = "conflict" — ban hien tai tren server.</summary>
    public PromptDetailResponse? Remote { get; set; }

    public Guid? ConflictId { get; set; }

    /// <summary>
    /// Chi co gia tri khi Status = "rejected" — thong bao CHUNG CHUNG (khong lo chi tiet noi bo
    /// nhu "id nay dang thuoc space khac"), dung cho ca loi validate lan tu choi tham chieu
    /// cheo-space.
    /// </summary>
    public string? Error { get; set; }
}
