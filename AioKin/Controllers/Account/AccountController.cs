using AioKin.Common;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.Transfers.ProfileUser;
using AioKin.Models.ViewModel.Auth.User;
using AioKin.Services.Auth.Biometric;
using AioKin.Services.Auth.Email;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.Token;
using AioKin.Services.Auth.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AioKin.Controllers.Account;

/// <summary>
/// Tai khoan cua chinh nguoi dang dang nhap. Moi endpoint deu lay danh tinh tu token,
/// khong bao gio tu tham so — nhan userId tu client nghia la ai cung doc/sua duoc ho so
/// cua nguoi khac chi bang cach doi mot con so.
/// </summary>
[ApiController]
[Route("account")]
[Produces("application/json")]
[Authorize(Roles = Roles.CUSTOMER)]
public class AccountController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IEmailService _emailService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IBiometricAuthService _biometricAuthService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IUserService userService,
        IEmailService emailService,
        IRefreshTokenService refreshTokenService,
        IAccessTokenService accessTokenService,
        IBiometricAuthService biometricAuthService,
        ILogger<AccountController> logger)
    {
        _userService = userService;
        _emailService = emailService;
        _refreshTokenService = refreshTokenService;
        _accessTokenService = accessTokenService;
        _biometricAuthService = biometricAuthService;
        _logger = logger;
    }

    /// <summary>Ho so cua tai khoan dang dang nhap.</summary>
    [HttpGet("me")]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMe()
    {
        var user = await GetCurrentUserAsync();

        return user is null
            ? this.ToActionResult(OperationResult.Fail("NotFound", "Khong tim thay nguoi dung."))
            : Ok(OperationResult.Ok(data: UserMapper.ToLoginResponse(user)));
    }

    /// <summary>Cap nhat ho so. Chi cac truong duoc gui len moi bi thay doi.</summary>
    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest model)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return Unauthorized(OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        return this.ToActionResult(await _userService.UpdateProfileAsync(userUuid.Value, model));
    }

    /// <summary>Doi mat khau khi da dang nhap. Thu hoi moi phien khac sau khi doi thanh cong.</summary>
    [HttpPost("me/change-password")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest model)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
            return this.ToActionResult(OperationResult.Fail("NotFound", "Khong tim thay nguoi dung."));

        if (string.IsNullOrEmpty(user.PasswordHash))
            return this.ToActionResult(OperationResult.Fail("Forbidden",
                $"Tai khoan nay lien ket voi {user.SocialProvider ?? "mang xa hoi"} va chua co mat khau. "
                + "Dung luong quen mat khau de dat mat khau dau tien."));

        if (!PasswordHelper.VerifyPassword(model.CurrentPassword, user.PasswordHash, user.PasswordSalt))
            return this.ToActionResult(OperationResult.Fail("InvalidCredentials", "Mat khau hien tai khong dung."));

        PasswordHelper.CreatePasswordHash(model.NewPassword, out var hash, out var salt);
        if (!await _userService.UpdatePasswordAsync(user.Username, hash, salt))
            return this.ToActionResult(OperationResult.Fail("InternalError", "Khong cap nhat duoc mat khau."));

        await _refreshTokenService.RevokeAllAsync(user.UserCode);
        await _accessTokenService.RevokeAllForSubjectAsync(user.UserCode);

        // P20: doi mat khau khi dang dang nhap cung phai thu hoi TOAN BO dang ky sinh trac
        // cua user, giong duong ResetPassword — cung mot ly do "nghi ngo tai khoan bi lo".
        await _biometricAuthService.RevokeAllForUserAsync(user.UserCode);

        if (user.Email is not null)
            await _emailService.SendPasswordChangedNoticeAsync(user.Email, user.Username);

        _logger.LogInformation("Password changed for userCode={UserCode}", user.UserCode);
        return Ok(OperationResult.Ok("Doi mat khau thanh cong. Vui long dang nhap lai tren cac thiet bi khac."));
    }

    /// <summary>Danh sach thiet bi dang dang nhap cua tai khoan nay.</summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions()
    {
        var userCode = User.GetUserCode();
        if (string.IsNullOrEmpty(userCode))
            return Unauthorized(OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        // Claim session_token gio la hash (P11) — id cua chinh request nay suy truc tiep tu
        // do, khong can bam lai.
        var currentHash = User.GetSessionToken();
        var currentId = string.IsNullOrEmpty(currentHash) ? null : TokenHash.PublicId(currentHash);

        var sessions = await _accessTokenService.ListSessionsAsync(userCode);

        var response = sessions.Select(s => new SessionResponse
        {
            Id = s.Id,
            DeviceName = s.Session.DeviceName,
            Platform = s.Session.Platform,
            IssuedAtUnix = s.Session.IssuedAtUnix,
            IsCurrent = s.Id == currentId
        }).ToList();

        return Ok(OperationResult.Ok(data: response));
    }

    /// <summary>Dang xuat 1 thiet bi cu the: thu hoi access session va refresh token cung thiet bi do.</summary>
    [HttpDelete("sessions/{id}")]
    public async Task<IActionResult> DeleteSession(string id)
    {
        var userCode = User.GetUserCode();
        if (string.IsNullOrEmpty(userCode))
            return Unauthorized(OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        // RevokeByIdAsync tu kiem tra id co thuoc ve userCode nay khong va tra ve chinh
        // session vua bi xoa — khong can list roi tim lai (P8).
        var revoked = await _accessTokenService.RevokeByIdAsync(userCode, id);
        if (revoked is null)
            return NotFound(OperationResult.Fail("NotFound", "Khong tim thay phien dang nhap nay."));

        // Thiet bi khong xac dinh (DeviceId null, vd token cu tu truoc khi co truong nay)
        // thi bo qua buoc thu hoi theo thiet bi — khong co gi de khop, va khong duoc lam
        // rong toan bo danh sach refresh token cua user chi vi mot session thieu DeviceId.
        if (revoked.DeviceId is { } deviceId)
        {
            await _refreshTokenService.RevokeAllForDeviceAsync(userCode, deviceId);
            await _accessTokenService.RevokeForDeviceAsync(userCode, deviceId);

            // Finding 1 (SECURITY): xoa 1 session cu the cung phai thu hoi credential sinh
            // trac dang ky cho CUNG thiet bi do — token bi lo va tu dang ky duoc sinh trac
            // khong duoc song sot qua DELETE session. Ket qua bi bo qua co tinh: khong co
            // credential nao cho thiet bi nay la binh thuong (RevokeAsync tra Fail NotFound),
            // khong duoc lam hong request xoa session chi vi thiet bi chua tung dang ky sinh
            // trac.
            await _biometricAuthService.RevokeAsync(userCode, deviceId);
        }

        return Ok(OperationResult.Ok("Da dang xuat thiet bi."));
    }

    /// <summary>Gui email lien he toi bo phan ho tro tu tai khoan dang dang nhap.</summary>
    [HttpPost("me/contact")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Contact([FromBody] ContactRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (user?.Email is null)
            return this.ToActionResult(OperationResult.Fail("ValidationError",
                "Tai khoan chua co dia chi email de gui lien he."));

        return await _emailService.SendContactEmailAsync(user.Email, request.Subject, request.Message)
            ? Ok(OperationResult.Ok("Da gui email."))
            : this.ToActionResult(OperationResult.Fail("EmailSendFailed", "Khong gui duoc email. Vui long thu lai sau."));
    }

    private async Task<Data.Entities.Security.User?> GetCurrentUserAsync()
    {
        var userUuid = User.GetUserUuid();
        return userUuid is null ? null : await _userService.GetByUuidAsync(userUuid.Value);
    }
}

public class ContactRequest
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Tieu de la bat buoc.")]
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Noi dung la bat buoc.")]
    [System.ComponentModel.DataAnnotations.MaxLength(5000)]
    public string Message { get; set; } = string.Empty;
}
