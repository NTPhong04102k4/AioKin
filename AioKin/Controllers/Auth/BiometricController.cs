using AioKin.Common;
using AioKin.Models.InputModel.Auth.Biometric;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Services.Auth.Biometric;
using AioKin.Services.Auth.Token;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AioKin.Controllers.Auth;

/// <summary>
/// Dang nhap bang sinh trac hoc (Face ID/van tay). Server khong bao gio nhan du lieu sinh
/// trac — chi verify chu ky ECDSA cua mot challenge dung mot lan. Xem
/// docs/auth-opaque-tokens-biometric.md muc 5 cho toan bo chuoi buoc.
/// </summary>
[ApiController]
[Route("auth/biometric")]
[Produces("application/json")]
public class BiometricController : ControllerBase
{
    private readonly IBiometricAuthService _biometricAuthService;
    private readonly IAccessTokenService _accessTokenService;

    public BiometricController(IBiometricAuthService biometricAuthService, IAccessTokenService accessTokenService)
    {
        _biometricAuthService = biometricAuthService;
        _accessTokenService = accessTokenService;
    }

    /// <summary>Dang ky public key cua thiet bi hien tai. Can da dang nhap thuong truoc do.</summary>
    [HttpPost("register")]
    [Authorize(Roles = Roles.CUSTOMER)]
    public async Task<IActionResult> Register([FromBody] RegisterBiometricRequest request)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return Unauthorized(OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        // P6 (SECURITY, MUST): chi duoc (dang ky lai) credential cho DUNG thiet bi ma access
        // token dang dung duoc phat ra — doc DeviceId tu chinh session (session_token claim),
        // khong bao gio tin request.DeviceId de quyet dinh danh tinh thiet bi. Chan "token
        // ngan han bi lo => dang nhap sinh trac vinh vien cho mot thiet bi bat ky ke tan cong
        // chon".
        var sessionHash = User.GetSessionToken();
        var session = string.IsNullOrEmpty(sessionHash) ? null : await _accessTokenService.GetByHashAsync(sessionHash);
        if (session?.DeviceId is null || !string.Equals(session.DeviceId, request.DeviceId, StringComparison.Ordinal))
            return this.ToActionResult(OperationResult.Fail("Forbidden",
                "Chi duoc dang ky sinh trac cho chinh thiet bi cua phien dang nhap hien tai."));

        return this.ToActionResult(await _biometricAuthService.RegisterAsync(userUuid.Value, request));
    }

    /// <summary>Xin mot challenge de dang nhap bang sinh trac. Luon tra 200, bat ke thiet bi co dang ky hay chua.</summary>
    [HttpPost("challenge")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Challenge([FromBody] BiometricChallengeRequest request)
        // P9: bao trong OperationResult envelope chuan (spec muc 6) — khong tra bare object.
        => this.ToActionResult(await _biometricAuthService.ChallengeAsync(request));

    /// <summary>Xac thuc chu ky va phat token neu hop le.</summary>
    [HttpPost("verify")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Verify([FromBody] BiometricVerifyRequest request)
        => this.ToActionResult(await _biometricAuthService.VerifyAsync(request));

    /// <summary>Tat dang nhap sinh trac cho 1 thiet bi (mat may, doi thiet bi).</summary>
    [HttpDelete("{deviceId}")]
    [Authorize(Roles = Roles.CUSTOMER)]
    public async Task<IActionResult> Revoke(string deviceId)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return Unauthorized(OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        return this.ToActionResult(await _biometricAuthService.RevokeAsync(userUuid.Value, deviceId));
    }
}
