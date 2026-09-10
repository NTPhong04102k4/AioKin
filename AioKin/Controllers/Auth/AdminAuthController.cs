using AioKin.Common;
using AioKin.Models.InputModel.Auth.Admin;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Auth.Admin;
using AioKin.Services.Auth.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AioKin.Controllers.Auth;

/// <summary>
/// Xac thuc phia quan tri. Tach khoi /auth cua khach hang: Staff dang nhap bang bang
/// khac, khong co SSO, va co rang buoc rieng ve role.
/// </summary>
[ApiController]
[Route("auth/admin")]
[Produces("application/json")]
public class AdminAuthController : ControllerBase
{
    private readonly IAdminAuthService _authService;

    public AdminAuthController(IAdminAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>Dang nhap danh cho Staff / Admin / SuperAdmin.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-strict")]
    [ProducesResponseType<LoginAdminResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginAdminRequest request)
    {
        var result = await _authService.LoginAsync(request);

        return result is null
            ? Unauthorized(OperationResult.Fail("InvalidCredentials", "Username hoac mat khau khong dung."))
            : Ok(result);
    }

    /// <summary>Tao tai khoan nhan vien moi. Chi SuperAdmin.</summary>
    [HttpPost("staff")]
    [Authorize(Roles = Roles.SUPERADMIN)]
    public async Task<IActionResult> CreateStaff([FromBody] CreateStaffRequest request)
    {
        // Nguoi tao lay tu token chu khong tu body: de client tu khai createBy nghia la
        // ai cung ghi duoc ten nguoi khac vao vet kiem toan.
        var callerStaffId = User.GetStaffId();
        if (callerStaffId is null)
            return Unauthorized(OperationResult.Fail("Unauthorized", "Token thieu thong tin nhan vien."));

        var result = await _authService.CreateStaffAsync(request, callerStaffId.Value);

        return result.Success
            ? StatusCode(StatusCodes.Status201Created, result)
            : this.ToActionResult(result);
    }

    /// <summary>Doi mat khau nhan vien: chinh chu, hoac SuperAdmin doi cho bat ky ai.</summary>
    [HttpPut("staff/{staffId:int}/password")]
    [Authorize(Roles = $"{Roles.STAFF},{Roles.ADMIN},{Roles.SUPERADMIN}")]
    public async Task<IActionResult> UpdateStaffPassword(int staffId, [FromBody] UpdateStaffPasswordRequest request)
    {
        var username = User.GetUsername();
        if (string.IsNullOrEmpty(username))
            return Unauthorized(OperationResult.Fail("Unauthorized", "Token khong hop le hoac thieu thong tin dang nhap."));

        return this.ToActionResult(await _authService.UpdateStaffPasswordAsync(staffId, request, username));
    }

    /// <summary>
    /// Khoi phuc mat khau SuperAdmin bang ma khoi phuc. Chi hoat dong khi da cau hinh
    /// <c>Auth:SuperAdminRecoveryCode</c>; khong cau hinh thi endpoint tra 403.
    /// </summary>
    [HttpPost("superadmin/recover-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> RecoverSuperAdminPassword([FromBody] SuperAdminRecoverPasswordRequest request)
        => this.ToActionResult(await _authService.RecoverSuperAdminPasswordAsync(request));
}
