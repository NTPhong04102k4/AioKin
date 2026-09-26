using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Vault;

namespace AioKin.Services.Vault;

public interface ISyncService
{
    /// <param name="request">Danh sach entry can push, xem SyncPushRequest.cs.</param>
    /// <param name="callerDeviceId">
    /// DeviceId cua CHINH phien dang goi (tu session_token claim), khong bao gio tu body —
    /// xem SyncPushRequest.cs va SyncController.Push.
    /// </param>
    /// <param name="cancellationToken">Token huy request.</param>
    Task<OperationResult> PushAsync(SyncPushRequest request, string? callerDeviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /sync/pull — tra ve incremental (danh sach sync_log) hoac snapshot day du khi
    /// retention da het/khoi luong qua lon. Xem Task 3 trong progress.md cho toan bo ruling.
    /// </summary>
    /// <param name="spaceUuid">Space can dong bo.</param>
    /// <param name="since">Cursor lan pull truoc (0 = dong bo tu dau).</param>
    /// <param name="callerDeviceId">
    /// DeviceId cua CHINH phien dang goi (tu session_token claim) — dung de suppress echo (khong
    /// tra lai cho chinh thiet bi vua push thay doi cua no), cung nguon voi SyncController.Push.
    /// </param>
    /// <param name="cancellationToken">Token huy request.</param>
    Task<OperationResult> PullAsync(Guid spaceUuid, long since, string? callerDeviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// POST /sync/conflicts/{id}/resolve. Xem Task 4 trong progress.md cho toan bo ruling
    /// (G11 security, P22 khong Last-Write-Wins, G9 dung/xoa, G8 tra NewVersion).
    /// </summary>
    /// <param name="conflictId">Id cua SyncConflict can xu ly.</param>
    /// <param name="request">"keep_local" | "keep_remote" | "merged" (+ MergedPayload neu merged).</param>
    /// <param name="callerDeviceId">
    /// DeviceId cua CHINH phien dang goi (tu session_token claim), khong bao gio tu body — cung
    /// nguon voi SyncController.Push/Pull (P16).
    /// </param>
    /// <param name="cancellationToken">Token huy request.</param>
    Task<OperationResult> ResolveConflictAsync(Guid conflictId, ResolveConflictRequest request, string? callerDeviceId, CancellationToken cancellationToken = default);
}
