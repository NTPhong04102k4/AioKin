namespace AioKin.Models.ViewModel.Vault;

/// <summary>
/// Fix round 1, finding 5: bao boc List&lt;SyncPushResponse&gt; kem tong ket cap-batch, de mot
/// client chi kiem tra OperationResult.Success cap tren cung KHONG THE bo lot cac entry
/// "rejected"/"conflict" nam rieng trong tung phan tu cua Results (OperationResult.Success van
/// la true — HTTP 200 — ke ca khi mot vai entry trong batch that bai, dung thiet ke).
/// </summary>
public class SyncPushBatchResponse
{
    public List<SyncPushResponse> Results { get; set; } = [];

    public int AppliedCount { get; set; }
    public int ConflictCount { get; set; }
    public int RejectedCount { get; set; }

    public bool HasFailures => ConflictCount > 0 || RejectedCount > 0;
}
