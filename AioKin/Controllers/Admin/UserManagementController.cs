using AioKin.Common;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.Transfers.ProfileUser;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.Token;
using AioKin.Services.Auth.User;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Admin;

/// <summary>Quan ly tai khoan khach hang tu phia quan tri.</summary>
[ApiController]
[Route("admin/users")]
[Produces("application/json")]
[Authorize(Roles = $"{Roles.STAFF},{Roles.ADMIN},{Roles.SUPERADMIN}")]
public class UserManagementController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IAccessTokenService _accessTokenService;

    public UserManagementController(
        IUserService userService,
        IRefreshTokenService refreshTokenService,
        IAccessTokenService accessTokenService)
    {
        _userService = userService;
        _refreshTokenService = refreshTokenService;
        _accessTokenService = accessTokenService;
    }

    /// <summary>Danh sach nguoi dung, co phan trang, tim kiem va loc theo khoang thoi gian.</summary>
    [HttpGet]
    public async Task<IActionResult> GetUsers([FromQuery] UserListQueryRequest query)
        => Ok(OperationResult.Ok(data: await _userService.GetUsersAsync(query)));

    /// <summary>Nguoi dung dang ky trong 7 ngay gan nhat.</summary>
    [HttpGet("recent/7-days")]
    public Task<IActionResult> GetRecent7Days([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => GetByPeriod("7d", page, pageSize);

    /// <summary>Nguoi dung dang ky trong 30 ngay gan nhat.</summary>
    [HttpGet("recent/30-days")]
    public Task<IActionResult> GetRecent30Days([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => GetByPeriod("30d", page, pageSize);

    /// <summary>Chi tiet mot nguoi dung theo UserUUID.</summary>
    [HttpGet("{userUuid:guid}")]
    public async Task<IActionResult> GetById(Guid userUuid)
    {
        var user = await _userService.GetByUuidAsync(userUuid);

        return user is null
            ? this.ToActionResult(OperationResult.Fail("NotFound", "Khong tim thay nguoi dung."))
            : Ok(OperationResult.Ok(data: UserMapper.ToListItem(user)));
    }

    /// <summary>
    /// Khoa hoac mo khoa tai khoan. Khoa xong thu hoi luon refresh token, neu khong
    /// nguoi dung van tu cap duoc access token moi cho toi khi token cu het han.
    /// </summary>
    [HttpPatch("{userUuid:guid}/lock")]
    [Authorize(Roles = $"{Roles.ADMIN},{Roles.SUPERADMIN}")]
    public async Task<IActionResult> SetLock(Guid userUuid, [FromBody] UserLockRequest request)
    {
        var result = await _userService.SetLockStateAsync(userUuid, request);

        if (result.Success && request.IsLocked)
            await RevokeSessionsAsync(userUuid);

        return this.ToActionResult(result);
    }

    /// <summary>Kich hoat hoac vo hieu hoa tai khoan.</summary>
    [HttpPatch("{userUuid:guid}/status")]
    [Authorize(Roles = $"{Roles.ADMIN},{Roles.SUPERADMIN}")]
    public async Task<IActionResult> SetStatus(Guid userUuid, [FromBody] UserStatusRequest request)
    {
        var result = await _userService.SetActiveStateAsync(userUuid, request);

        if (result.Success && !request.IsActive)
            await RevokeSessionsAsync(userUuid);

        return this.ToActionResult(result);
    }

    private async Task<IActionResult> GetByPeriod(string period, int page, int pageSize)
    {
        var result = await _userService.GetUsersAsync(new UserListQueryRequest
        {
            Page = page,
            PageSize = pageSize,
            Period = period
        });

        return Ok(OperationResult.Ok(data: result));
    }

    private async Task RevokeSessionsAsync(Guid userUuid)
    {
        var user = await _userService.GetByUuidAsync(userUuid);
        if (user is not null)
        {
            await _refreshTokenService.RevokeAllAsync(user.UserCode);
            await _accessTokenService.RevokeAllForSubjectAsync(user.UserCode);
        }
    }
}
