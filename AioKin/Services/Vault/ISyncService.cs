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
}
