using System.Text.Json;
using AioKin.Common;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.Transfers.ProfileUser;
using AioKin.Models.ViewModel.Auth.User;
using AioKin.Services.Auth.Email;
using AioKin.Services.Auth.OAuth;
using AioKin.Services.Auth.Biometric;
using AioKin.Services.Auth.Otp;
using AioKin.Services.Auth.PasswordUser;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.Token;
using AioKin.Services.Auth.User;
using AioKin.Services.Common.Cache;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AioKin.Controllers.Auth;

/// <summary>
/// Ba duong dang nhap cua khach hang deu ket thuc o cung mot cap token:
/// username/password, Google va Facebook.
/// </summary>
[ApiController]
[Route("auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    /// <summary>Cookie tam giu ket qua tu nha cung cap SSO giua callback va buoc finalize.</summary>
    public const string ExternalCookieScheme = "ExternalTempCookie";

    /// <summary>So lan nhap sai lien tiep truoc khi khoa tai khoan.</summary>
    private const int MaxFailedAttempts = 5;

    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private readonly IConfiguration _configuration;
    private readonly IUserService _userService;
    private readonly IOAuthService _oauthService;
    private readonly IEmailService _emailService;
    private readonly IOtpService _otpService;
    private readonly ITemporaryPasswordService _tempPasswordService;
    private readonly IRedisService _redis;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IBiometricAuthService _biometricAuthService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IConfiguration configuration,
        IUserService userService,
        IOAuthService oauthService,
        IEmailService emailService,
        IOtpService otpService,
        ITemporaryPasswordService tempPasswordService,
        IRedisService redis,
        IRefreshTokenService refreshTokenService,
        IAccessTokenService accessTokenService,
        IBiometricAuthService biometricAuthService,
        ILogger<AuthController> logger)
    {
        _configuration = configuration;
        _userService = userService;
        _oauthService = oauthService;
        _emailService = emailService;
        _otpService = otpService;
        _tempPasswordService = tempPasswordService;
        _redis = redis;
        _refreshTokenService = refreshTokenService;
        _accessTokenService = accessTokenService;
        _biometricAuthService = biometricAuthService;
        _logger = logger;
    }

    // ─── Dang nhap bang mat khau ──────────────────────────────────────────────

    /// <summary>Dang nhap bang username, email hoac so dien thoai. Tra ve access token va refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-strict")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest model)
    {
        var user = await _userService.GetByUsernameOrEmailAsync(model.UsernameOrPhoneOrEmail);

        // Khong phan biet "khong ton tai" voi "sai mat khau": phan biet cho phep do xem
        // email/username nao co trong he thong.
        if (user is null)
            return Unauthorized(OperationResult.Fail("InvalidCredentials", "Thong tin dang nhap khong dung."));

        if (user.IsLocked && user.LockUntil.HasValue)
        {
            if (DateTime.UtcNow < user.LockUntil.Value)
            {
                var remaining = Math.Ceiling((user.LockUntil.Value - DateTime.UtcNow).TotalMinutes);
                return Unauthorized(OperationResult.Fail("AccountLocked",
                    $"Tai khoan dang bi khoa. Vui long thu lai sau {remaining} phut."));
            }

            // Het han khoa — mo khoa roi cho di tiep trong cung request nay.
            await _userService.RecordLoginAttemptAsync(user.UserUUID, 0, false, null, null);
            user.IsLocked = false;
            user.LoginAttempts = 0;
        }

        if (!user.IsActive)
            return Unauthorized(OperationResult.Fail("UserInactive", "Tai khoan da bi vo hieu hoa."));

        // Tai khoan tao tu SSO chua tung dat mat khau — noi ro thay vi "sai mat khau",
        // neu khong nguoi dung se thu mai mot mat khau chua bao gio ton tai.
        if (string.IsNullOrEmpty(user.PasswordHash))
            return Unauthorized(OperationResult.Fail("InvalidCredentials",
                $"Tai khoan nay dang nhap bang {user.SocialProvider ?? "mang xa hoi"}. Dung 'quen mat khau' de dat mat khau."));

        if (!PasswordHelper.VerifyPassword(model.Password, user.PasswordHash, user.PasswordSalt))
        {
            var attempts = user.LoginAttempts + 1;
            var shouldLock = attempts >= MaxFailedAttempts;

            await _userService.RecordLoginAttemptAsync(
                user.UserUUID, attempts, shouldLock, shouldLock ? DateTime.UtcNow.Add(LockoutDuration) : null, null);

            _logger.LogWarning("Failed login for userCode={UserCode}, attempts={Attempts}", user.UserCode, attempts);

            return Unauthorized(shouldLock
                ? OperationResult.Fail("AccountLocked", "Tai khoan da bi khoa do nhap sai qua nhieu lan.")
                : OperationResult.Fail("InvalidCredentials", "Thong tin dang nhap khong dung."));
        }

        await _userService.RecordLoginAttemptAsync(user.UserUUID, 0, false, null, DateTime.UtcNow);

        var device = DeviceInfo.Resolve(model.DeviceId, model.DeviceName, model.Platform);

        return Ok(new TokenResponse
        {
            AccessToken = await _accessTokenService.CreateForCustomerAsync(user, device),
            RefreshToken = await _refreshTokenService.GenerateAsync(user.UserCode, Roles.CUSTOMER, device),
            ExpiresIn = _accessTokenService.AccessTokenLifetimeSeconds,
            TokenType = "Bearer",
            Scope = Roles.CUSTOMER
        });
    }

    // ─── Dang ky ──────────────────────────────────────────────────────────────

    /// <summary>Dang ky tai khoan. Chua tao user ngay — gui OTP ve email de xac thuc truoc.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest model)
    {
        if (await _userService.EmailExistsAsync(model.Email))
            return this.ToActionResult(OperationResult.Fail("EmailExists", "Email nay da duoc su dung."));

        if (await _userService.UsernameExistsAsync(model.Username))
            return this.ToActionResult(OperationResult.Fail("UsernameExists", "Username nay da duoc su dung."));

        PasswordHelper.CreatePasswordHash(model.Password, out var passwordHash, out var passwordSalt);

        // Giu ban dang ky trong Redis thay vi ghi user chua xac thuc vao database: email
        // chua verify khong duoc chiem cho trong bang users, va khong can don rac sau nay.
        var saved = await _redis.SetAsync(
            RedisKeys.Registration(model.Email),
            new PendingRegistration
            {
                Username = model.Username,
                Email = model.Email,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt
            },
            RedisTtl.Registration);

        if (!saved)
        {
            _logger.LogError("Failed to persist registration data for email={Email}", model.Email);
            return this.ToActionResult(OperationResult.Fail("InternalError", "Khong xu ly duoc dang ky. Vui long thu lai."));
        }

        string otpCode;
        try
        {
            otpCode = await _otpService.GenerateOtpAsync(model.Email);
        }
        catch (InvalidOperationException ex)
        {
            await _redis.DeleteAsync(RedisKeys.Registration(model.Email));
            _logger.LogError(ex, "OTP generation failed for email={Email}", model.Email);
            return this.ToActionResult(OperationResult.Fail("OtpGenerationFailed", "Khong tao duoc OTP. Vui long thu lai."));
        }

        if (!await _emailService.SendOtpEmailAsync(model.Email, otpCode, model.Username))
        {
            _logger.LogError("Failed to send OTP email for email={Email}", model.Email);
            return this.ToActionResult(OperationResult.Fail("EmailSendFailed",
                "Khong gui duoc email xac thuc. Vui long thu lai sau."));
        }

        return Ok(OperationResult.Ok("Ma OTP da duoc gui den email cua ban. Vui long kiem tra hop thu."));
    }

    /// <summary>Xac thuc OTP de kich hoat tai khoan vua dang ky. Tra ve token luon nen khong phai dang nhap lai.</summary>
    [HttpPost("verify-otp")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest model)
    {
        if (!await _otpService.VerifyOtpAsync(model.Email, model.OtpCode))
            return this.ToActionResult(OperationResult.Fail("InvalidOtp", "Ma OTP khong hop le hoac da het han."));

        var registration = await _redis.GetAsync<PendingRegistration>(RedisKeys.Registration(model.Email));
        if (registration is null)
            return this.ToActionResult(OperationResult.Fail("RegistrationDataNotFound",
                "Khong tim thay thong tin dang ky. Vui long dang ky lai."));

        AioKin.Data.Entities.Security.User createdUser;
        try
        {
            createdUser = await _userService.CreateUserAsync(UserMapper.FromRegistration(registration));
        }
        catch (InvalidOperationException ex)
        {
            // Co the co nguoi khac da chiem email/username trong 15 phut cho xac thuc.
            _logger.LogWarning(ex, "User creation failed after OTP verify for email={Email}", model.Email);
            return this.ToActionResult(OperationResult.Fail("Conflict", ex.Message));
        }

        await _redis.DeleteAsync(RedisKeys.Registration(model.Email));

        var device = DeviceInfo.Resolve(model.DeviceId, model.DeviceName, model.Platform);
        var accessToken = await _accessTokenService.CreateForCustomerAsync(createdUser, device);
        var refreshToken = await _refreshTokenService.GenerateAsync(createdUser.UserCode, Roles.CUSTOMER, device);

        // Email chao mung khong duoc lam hong dang ky — gui that bai thi chi ghi log.
        if (createdUser.Email is not null)
            await _emailService.SendWelcomeEmailAsync(createdUser.Email, createdUser.Username);

        return StatusCode(StatusCodes.Status201Created, OperationResult.Ok("Dang ky thanh cong.", new
        {
            accessToken,
            refreshToken,
            expiresIn = _accessTokenService.AccessTokenLifetimeSeconds,
            tokenType = "Bearer",
            user = UserMapper.ToLoginResponse(createdUser)
        }));
    }

    /// <summary>Gui lai ma OTP dang ky. Co cooldown 60 giay moi email.</summary>
    [HttpPost("resend-otp")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpRequest model)
    {
        var cooldownKey = RedisKeys.OtpCooldown(model.Email);

        // SET NX: gianh khoa cooldown mot cach nguyen tu. Kiem tra roi moi set se ho race
        // khi nguoi dung bam gui lai hai lan lien tiep.
        if (!await _redis.SetIfNotExistsAsync(cooldownKey, "1", RedisTtl.OtpCooldown))
            return this.ToActionResult(OperationResult.Fail("TooManyRequests",
                "Vui long doi 60 giay truoc khi yeu cau ma moi."));

        if (await _userService.EmailExistsAsync(model.Email))
            return this.ToActionResult(OperationResult.Fail("EmailExists", "Email nay da duoc dang ky."));

        var registration = await _redis.GetAsync<PendingRegistration>(RedisKeys.Registration(model.Email));
        if (registration is null)
            return this.ToActionResult(OperationResult.Fail("RegistrationDataNotFound",
                "Phien dang ky da het han. Vui long dang ky lai."));

        string otpCode;
        try
        {
            otpCode = await _otpService.GenerateOtpAsync(model.Email);
        }
        catch (InvalidOperationException ex)
        {
            // Khong tao duoc OTP thi go cooldown de nguoi dung thu lai ngay.
            await _redis.DeleteAsync(cooldownKey);
            _logger.LogError(ex, "OTP generation failed for email={Email}", model.Email);
            return this.ToActionResult(OperationResult.Fail("OtpGenerationFailed", "Khong tao duoc OTP. Vui long thu lai."));
        }

        if (!await _emailService.SendOtpEmailAsync(model.Email, otpCode, registration.Username))
            return this.ToActionResult(OperationResult.Fail("EmailSendFailed",
                "Khong gui duoc email xac thuc. Vui long thu lai sau."));

        return Ok(OperationResult.Ok("Ma OTP moi da duoc gui den email cua ban."));
    }

    // ─── Quen mat khau ────────────────────────────────────────────────────────

    /// <summary>Buoc 1: gui OTP dat lai mat khau. Luon tra 200 du email co ton tai hay khong.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest model)
    {
        var user = await _userService.GetByUsernameOrEmailAsync(model.Email);

        // Tra ve cung mot phan hoi du email co ton tai hay khong — khac nhau se bien
        // endpoint nay thanh cong cu do xem dia chi nao da dang ky.
        const string genericMessage = "Neu email ton tai, ban se nhan duoc huong dan dat lai mat khau.";

        if (user is null)
            return Ok(OperationResult.Ok(genericMessage));

        try
        {
            var otpCode = await _otpService.GenerateOtpAsync(model.Email);
            await _emailService.SendPasswordResetOtpEmailAsync(model.Email, otpCode, user.Username);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Password reset OTP generation failed for email={Email}", model.Email);
        }

        return Ok(OperationResult.Ok(genericMessage));
    }

    /// <summary>Buoc 2: xac thuc OTP, nhan mat khau tam qua email (song 3 phut).</summary>
    [HttpPost("forgot-password/verify-otp")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> VerifyOtpForPassword([FromBody] VerifyOtpForPasswordRequest model)
    {
        if (!await _otpService.VerifyOtpAsync(model.Email, model.OtpCode))
            return this.ToActionResult(OperationResult.Fail("InvalidOtp", "Ma OTP khong hop le hoac da het han."));

        var user = await _userService.GetByUsernameOrEmailAsync(model.Email);
        if (user is null)
            return this.ToActionResult(OperationResult.Fail("NotFound", "Khong tim thay nguoi dung."));

        var tempPassword = await _tempPasswordService.GenerateTemporaryPasswordAsync(model.Email);

        // Mat khau tam di qua email chu khong nam trong response: OTP co the bi doc tren
        // duong truyen hoac trong log, con hom thu thi chi chu tai khoan mo duoc.
        if (!await _emailService.SendTemporaryPasswordEmailAsync(model.Email, tempPassword, user.Username))
            return this.ToActionResult(OperationResult.Fail("EmailSendFailed",
                "Khong gui duoc mat khau tam. Vui long thu lai sau."));

        return Ok(OperationResult.Ok("Mat khau tam da duoc gui den email cua ban.", new
        {
            step = "temp_password_sent",
            expiresInMinutes = (int)RedisTtl.TempPassword.TotalMinutes
        }));
    }

    /// <summary>Buoc 3: dung mat khau tam de dat mat khau moi.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest model)
    {
        if (!await _tempPasswordService.VerifyTemporaryPasswordAsync(model.Email, model.TemporaryPassword))
            return this.ToActionResult(OperationResult.Fail("InvalidTemporaryPassword",
                "Mat khau tam khong hop le hoac da het han."));

        var user = await _userService.GetByUsernameOrEmailAsync(model.Email);
        if (user is null)
            return this.ToActionResult(OperationResult.Fail("NotFound", "Khong tim thay nguoi dung."));

        PasswordHelper.CreatePasswordHash(model.NewPassword, out var hash, out var salt);
        if (!await _userService.UpdatePasswordAsync(user.Username, hash, salt))
            return this.ToActionResult(OperationResult.Fail("InternalError", "Khong cap nhat duoc mat khau."));

        // Doi mat khau phai duoi moi phien cu — nguoi dung dat lai mat khau thuong la vi
        // nghi ngo tai khoan bi lo.
        await _refreshTokenService.RevokeAllAsync(user.UserCode);
        await _accessTokenService.RevokeAllForSubjectAsync(user.UserCode);

        // P20: dat lai mat khau cung phai thu hoi TOAN BO dang ky sinh trac cua user — mat
        // khau bi lo (ly do dan den reset) thi ke chiem duoc no cung khong duoc giu lai duong
        // dang nhap sinh trac da dang ky truoc do.
        await _biometricAuthService.RevokeAllForUserAsync(user.UserCode);

        if (user.Email is not null)
            await _emailService.SendPasswordChangedNoticeAsync(user.Email, user.Username);

        return Ok(OperationResult.Ok("Dat lai mat khau thanh cong. Vui long dang nhap lai."));
    }

    // ─── Token ────────────────────────────────────────────────────────────────

    /// <summary>Doi refresh token lay cap token moi. Token cu bi thu hoi trong cung request (rotation).</summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest model)
    {
        var payload = await _refreshTokenService.ValidateAsync(model.RefreshToken);
        if (payload is null)
            return this.ToActionResult(OperationResult.Fail("InvalidRefreshToken",
                "Refresh token khong hop le hoac da het han. Vui long dang nhap lai."));

        var (subject, role, payloadDeviceId, payloadDeviceName, payloadPlatform) = payload;

        // Thu hoi truoc khi cap token moi: neu cap truoc roi moi thu hoi va co su co o
        // giua, ca hai token deu con song.
        await _refreshTokenService.RevokeAsync(model.RefreshToken);

        // Uu tien thiet bi da luu trong PAYLOAD (tu luc dang nhap/refresh truoc), chi roi ve
        // truong client gui kem request khi payload khong co: neu uu tien request, ke dang giu
        // refresh token cua thiet bi A co the tu xung minh la thiet bi B (gui deviceId cua B
        // trong body) va lam token moi cuop lay dinh danh cua B — lan RevokeForDeviceAsync/
        // RevokeAllForDeviceAsync ke tiep tren "thiet bi B" se giet nham phien that cua B.
        // Payload-first cung tranh sinh unknown-<guid> MOI moi lan refresh cho client cu/native
        // khong gui lai deviceId (Expo gap G7).
        var deviceId = string.IsNullOrWhiteSpace(payloadDeviceId) ? model.DeviceId : payloadDeviceId;
        var deviceName = string.IsNullOrWhiteSpace(payloadDeviceName) ? model.DeviceName : payloadDeviceName;
        var platform = string.IsNullOrWhiteSpace(payloadPlatform) ? model.Platform : payloadPlatform;
        var device = DeviceInfo.Resolve(deviceId, deviceName, platform);

        // Bo access session cu cua chinh thiet bi nay truoc khi cap cai moi — neu khong no
        // van song toi khi het TTL, khien danh sach phien hien thi hai ban ghi cho cung mot
        // thiet bi sau moi lan refresh.
        await _accessTokenService.RevokeForDeviceAsync(subject, device.DeviceId!);

        string accessToken;

        if (string.Equals(role, Roles.CUSTOMER, StringComparison.Ordinal))
        {
            // Voi khach hang, subject la UserCode.
            var user = await _userService.GetByUserCodeAsync(subject);
            if (user is null || !user.IsActive || user.IsLocked)
                return this.ToActionResult(OperationResult.Fail("UserInactive", "Tai khoan khong con hoat dong."));

            accessToken = await _accessTokenService.CreateForCustomerAsync(user, device);
        }
        else
        {
            // Voi tai khoan quan tri, subject la Username cua Staff.
            var staff = await _userService.GetStaffByUsernameAsync(subject);
            if (staff is null || !staff.IsActive)
                return this.ToActionResult(OperationResult.Fail("UserInactive", "Tai khoan khong con hoat dong."));

            // Doc lai role tu database thay vi tin role trong refresh token: quyen co the
            // da bi ha ke tu luc dang nhap.
            accessToken = await _accessTokenService.CreateForStaffAsync(staff, staff.Role?.RoleName ?? role, device);
            role = staff.Role?.RoleName ?? role;
        }

        return Ok(new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = await _refreshTokenService.GenerateAsync(subject, role, device),
            ExpiresIn = _accessTokenService.AccessTokenLifetimeSeconds,
            TokenType = "Bearer",
            Scope = role
        });
    }

    /// <summary>
    /// Dang xuat: thu hoi access token cua request nay va refresh token cua CUNG thiet bi do.
    /// Logout phai dong nghia voi DELETE /account/sessions/{id} cho chinh phien nay — client
    /// bo trong refreshToken (vd khong con giu trong bo nho) khong duoc phep de refresh token
    /// cua thiet bi song sot, vi khong thi "dang xuat" tren UI van con dang nhap duoc lai bang
    /// refresh token cu.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request = null)
    {
        // Claim session_token gio la hash (P11), khong con raw token — doc session truoc de
        // biet DeviceId (can cho nhanh fallback ben duoi) roi moi revoke thang bang hash.
        var sessionHash = User.GetSessionToken();
        AccessTokenSession? session = null;
        if (!string.IsNullOrEmpty(sessionHash))
        {
            session = await _accessTokenService.GetByHashAsync(sessionHash);
            await _accessTokenService.RevokeByHashAsync(sessionHash);
        }

        if (!string.IsNullOrEmpty(request?.RefreshToken))
            await _refreshTokenService.RevokeAsync(request.RefreshToken);
        else if (session?.DeviceId is { } deviceId)
            await _refreshTokenService.RevokeAllForDeviceAsync(session.Subject, deviceId);

        return Ok(OperationResult.Ok("Dang xuat thanh cong."));
    }

    /// <summary>Dang xuat khoi moi thiet bi: thu hoi toan bo access va refresh token cua tai khoan.</summary>
    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll()
    {
        var subject = User.GetUserCode() ?? User.GetUsername();
        if (string.IsNullOrEmpty(subject))
            return Unauthorized(OperationResult.Fail("Unauthorized", "Token thieu thong tin dinh danh."));

        await _accessTokenService.RevokeAllForSubjectAsync(subject);
        await _refreshTokenService.RevokeAllAsync(subject);

        // Finding 1 (SECURITY, overrides P20): logout-all cung phai thu hoi TOAN BO credential
        // sinh trac cua user — mot access/refresh token bi lo va tu dang ky duoc sinh trac cho
        // mot thiet bi khong duoc song sot qua "dang xuat khoi tat ca thiet bi". Subject o day
        // co the la UserCode (customer) hoac Username (staff) — RevokeAllForUserAsync tu
        // khong lam gi neu khong tim thay user theo userCode (nhanh staff), nen goi vo dieu
        // kien o day la an toan cho ca hai nhanh, giong cach AuthController.ResetPassword va
        // AccountController.ChangePassword (Task 5) da lam.
        await _biometricAuthService.RevokeAllForUserAsync(subject);

        return Ok(OperationResult.Ok("Da dang xuat khoi tat ca thiet bi."));
    }

    // ─── SSO: Google ──────────────────────────────────────────────────────────

    /// <summary>Bat dau dang nhap Google. Callback do middleware xu ly tai /auth/callback/google.</summary>
    [HttpGet("login/google")]
    [AllowAnonymous]
    public IActionResult GoogleLogin()
    {
        // RedirectUri la noi ASP.NET Core quay ve SAU khi da xu ly CallbackPath — khong
        // duoc trung CallbackPath, neu khong se thanh vong lap.
        var props = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(GoogleFinalize)) ?? "/auth/finalize/google"
        };
        return Challenge(props, GoogleDefaults.AuthenticationScheme);
    }

    /// <summary>Buoc cuoi cua luong Google: doc cookie tam, cap access token, gui ve popup.</summary>
    [HttpGet("finalize/google")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleFinalize()
    {
        var auth = await HttpContext.AuthenticateAsync(ExternalCookieScheme);
        if (!auth.Succeeded || auth.Principal?.Identity?.IsAuthenticated != true)
            return BuildOAuthPopupHtml(
                OAuthResult.Fail("Phien dang nhap Google da het han hoac bi huy."),
                "GOOGLE_LOGIN_SUCCESS", "GOOGLE_LOGIN_ERROR");

        var result = await _oauthService.CompleteGoogleLoginAsync(auth);
        await HttpContext.SignOutAsync(ExternalCookieScheme);
        return BuildOAuthPopupHtml(result, "GOOGLE_LOGIN_SUCCESS", "GOOGLE_LOGIN_ERROR");
    }

    // ─── SSO: Facebook ────────────────────────────────────────────────────────

    /// <summary>Bat dau dang nhap Facebook. Callback do middleware xu ly tai /auth/callback/facebook.</summary>
    [HttpGet("login/facebook")]
    [AllowAnonymous]
    public IActionResult FacebookLogin()
    {
        var props = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(FacebookFinalize)) ?? "/auth/finalize/facebook"
        };
        return Challenge(props, FacebookDefaults.AuthenticationScheme);
    }

    /// <summary>Buoc cuoi cua luong Facebook: doc cookie tam, cap access token, gui ve popup.</summary>
    [HttpGet("finalize/facebook")]
    [AllowAnonymous]
    public async Task<IActionResult> FacebookFinalize()
    {
        var auth = await HttpContext.AuthenticateAsync(ExternalCookieScheme);
        if (!auth.Succeeded || auth.Principal?.Identity?.IsAuthenticated != true)
            return BuildOAuthPopupHtml(
                OAuthResult.Fail("Phien dang nhap Facebook da het han hoac bi huy."),
                "FACEBOOK_LOGIN_SUCCESS", "FACEBOOK_LOGIN_ERROR");

        var result = await _oauthService.CompleteFacebookLoginAsync(auth);
        await HttpContext.SignOutAsync(ExternalCookieScheme);
        return BuildOAuthPopupHtml(result, "FACEBOOK_LOGIN_SUCCESS", "FACEBOOK_LOGIN_ERROR");
    }

    /// <summary>
    /// Trang trung gian trong popup: postMessage ket qua ve cua so cha roi tu dong dong.
    /// </summary>
    private ContentResult BuildOAuthPopupHtml(OAuthResult result, string successType, string errorType)
    {
        var targetOrigin = ResolveFrontendOrigin();

        var payload = result.Success
            ? JsonSerializer.Serialize(new
            {
                type = successType,
                accessToken = result.Token,
                refreshToken = result.RefreshToken,
                expiresIn = result.ExpiresIn,
                tokenType = "Bearer",
                user = result.UserData
            })
            : JsonSerializer.Serialize(new { type = errorType, error = result.ErrorMessage });

        var closeDelayMs = result.Success ? 300 : 2000;
        var statusText = result.Success ? "Dang nhap thanh cong. Dang dong cua so..." : "Dang nhap that bai. Ban co the dong cua so nay.";

        // JsonSerializer.Serialize da escape du lieu, nhung </script> trong mot chuoi JSON
        // van ket thuc the script som — thay bang dang escape unicode de an toan khi nhung.
        var safePayload = payload.Replace("</", @"<\/");

        var html = $$"""
                     <!doctype html>
                     <html><head><meta charset="utf-8"><title>AioKin</title></head>
                     <body style="font-family:system-ui,sans-serif;text-align:center;margin-top:2rem">
                       <p>{{statusText}}</p>
                       <script>
                         (function () {
                           var payload = {{safePayload}};
                           if (window.ReactNativeWebView && typeof window.ReactNativeWebView.postMessage === 'function') {
                             window.ReactNativeWebView.postMessage(typeof payload === 'string' ? payload : JSON.stringify(payload));
                           }
                           if (window.opener && typeof window.opener.postMessage === 'function') {
                             window.opener.postMessage(payload, {{JsonSerializer.Serialize(targetOrigin)}});
                           }
                           setTimeout(function () { window.close(); }, {{closeDelayMs}});
                         })();
                       </script>
                     </body></html>
                     """;

        return Content(html, "text/html");
    }

    /// <summary>
    /// Origin nhan postMessage. Uu tien Frontend:Origin, sau do origin dau tien trong
    /// Cors:AllowedOrigins — cung nguon ma Program.cs dung de cau hinh CORS, de hai noi
    /// khong the lech nhau.
    ///
    /// Chi roi ve "*" khi khong cau hinh gi. Trong Production dieu do nghia la giao token
    /// cho bat ky trang nao mo duoc popup nay, nen phai cau hinh that.
    /// </summary>
    private string ResolveFrontendOrigin()
    {
        var configured = _configuration["Frontend:Origin"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');

        var allowed = _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?.Select(o => o.TrimEnd('/'))
            .FirstOrDefault(o => !string.IsNullOrWhiteSpace(o));

        if (!string.IsNullOrWhiteSpace(allowed))
            return allowed;

        _logger.LogWarning("Chua cau hinh Frontend:Origin hay Cors:AllowedOrigins — postMessage se dung '*'.");
        return "*";
    }
}
