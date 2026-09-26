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

    /// <summary>
    /// Incremental (hoac snapshot khi retention/khoi luong vuot nguong) — xem
    /// SyncService.PullAsync va Task 3 trong progress.md.
    /// </summary>
    [HttpGet("pull")]
    public async Task<IActionResult> Pull([FromQuery] Guid spaceUuid, [FromQuery] long since, CancellationToken cancellationToken)
    {
        // Cung nguon DeviceId voi Push (P16/carry-forward): luon tu session cua chinh caller,
        // khong bao gio tu query string — dung de echo-suppress dung (user, device) cua no.
        var sessionHash = User.GetSessionToken();
        var session = string.IsNullOrEmpty(sessionHash) ? null : await _accessTokenService.GetByHashAsync(sessionHash);

        return this.ToActionResult(await _syncService.PullAsync(spaceUuid, since, session?.DeviceId, cancellationToken));
    }

    /// <summary>
    /// Task 4: KHONG dung Last-Write-Wins -- ca 2 ban duoc giu, nguoi dung tu chon
    /// keep_local/keep_remote/merged. Xem SyncService.ResolveConflictAsync va progress.md muc
    /// "Task 4" cho toan bo ruling (G11 security, P22, G8, G9).
    /// </summary>
    [HttpPost("conflicts/{conflictId:guid}/resolve")]
    public async Task<IActionResult> ResolveConflict(Guid conflictId, [FromBody] ResolveConflictRequest request, CancellationToken cancellationToken)
    {
        // Cung nguon DeviceId voi Push/Pull (P16/carry-forward): luon tu session cua chinh
        // caller, khong bao gio tu body.
        var sessionHash = User.GetSessionToken();
        var session = string.IsNullOrEmpty(sessionHash) ? null : await _accessTokenService.GetByHashAsync(sessionHash);

        return this.ToActionResult(await _syncService.ResolveConflictAsync(conflictId, request, session?.DeviceId, cancellationToken));
    }
}
