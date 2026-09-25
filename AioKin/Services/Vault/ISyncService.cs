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
}
