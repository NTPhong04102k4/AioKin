using AioKin.Common;
using AioKin.Models.InputModel.Vault;
using AioKin.Services.Auth.Token;
using AioKin.Services.Vault;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Vault;

/// <summary>
/// Duy nhat mot duong ghi (create/update/delete) cho Prompt — xem spec muc 6.2. Pull/resolve o
/// Task 3-4.
/// </summary>
[ApiController]
[Route("sync")]
[Produces("application/json")]
[Authorize(Roles = Roles.CUSTOMER)]
public class SyncController : ControllerBase
{
    private readonly ISyncService _syncService;
    private readonly IAccessTokenService _accessTokenService;

    public SyncController(ISyncService syncService, IAccessTokenService accessTokenService)
    {
        _syncService = syncService;
        _accessTokenService = accessTokenService;
    }

    [HttpPost("push")]
    public async Task<IActionResult> Push([FromBody] SyncPushRequest request, CancellationToken cancellationToken)
    {
        // Carry-forward P16: DeviceId ghi vao Prompt.UpdatedDeviceId/sync.devices phai lay tu
        // CHINH phien dang nhap cua caller (session_token claim), khong bao gio tu mot truong
        // trong body ma client co the gia mao thanh thiet bi bat ky — cung cach lam voi
        // BiometricController.Register (P6).
        var sessionHash = User.GetSessionToken();
        var session = string.IsNullOrEmpty(sessionHash) ? null : await _accessTokenService.GetByHashAsync(sessionHash);

        return this.ToActionResult(await _syncService.PushAsync(request, session?.DeviceId, cancellationToken));
    }
}
