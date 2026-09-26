using AioKin.Common;
using AioKin.Data;
using AioKin.Data.Entities.Security;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Controllers.Security;

/// <summary>
/// Quan ly FCM Device Token cho push notification tren cac thiet bi cua nguoi dung.
/// </summary>
[ApiController]
[Route("device")]
[Produces("application/json")]
[Authorize]
public class DeviceController(AioKinDbContext dbContext, ILogger<DeviceController> logger) : ControllerBase
{
    private readonly AioKinDbContext _dbContext = dbContext;
    private readonly ILogger<DeviceController> _logger = logger;

    /// <summary>Dang ky hoac cap nhat FCM registration token cho thiet bi hien tai.</summary>
    [HttpPost("register-token")]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RegisterToken([FromBody] RegisterDeviceTokenRequest request)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return this.ToActionResult(OperationResult.Fail("Unauthorized", "Khong tim thay danh tinh nguoi dung."));

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserUUID == userUuid.Value);
        if (user is null)
            return this.ToActionResult(OperationResult.Fail("Unauthorized", "Tai khoan khong ton tai."));

        var existing = await _dbContext.DeviceTokens.FirstOrDefaultAsync(t => t.Token == request.Token);
        if (existing is not null)
        {
            existing.UserID = user.UserID;
            existing.Platform = string.IsNullOrWhiteSpace(request.Platform) ? existing.Platform : request.Platform.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(request.DeviceId))
                existing.DeviceId = request.DeviceId;
            existing.IsActive = true;
            existing.LastUsedAt = DateTime.UtcNow;
            _logger.LogInformation("Cap nhat FCM token cho user {UserId}, device {DeviceId}", user.UserID, existing.DeviceId);
        }
        else
        {
            var newToken = new DeviceToken
            {
                UserID = user.UserID,
                Token = request.Token,
                Platform = string.IsNullOrWhiteSpace(request.Platform) ? "android" : request.Platform.ToLowerInvariant(),
                DeviceId = request.DeviceId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow
            };
            _dbContext.DeviceTokens.Add(newToken);
            _logger.LogInformation("Dang ky FCM token moi cho user {UserId}, device {DeviceId}", user.UserID, request.DeviceId);
        }

        await _dbContext.SaveChangesAsync();
        return Ok(OperationResult.Ok("Dang ky device token thanh cong."));
    }

    /// <summary>Huy dang ky FCM token (khi logout hoac tat notification).</summary>
    [HttpDelete("token/{token}")]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveToken(string token)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return this.ToActionResult(OperationResult.Fail("Unauthorized", "Khong tim thay danh tinh nguoi dung."));

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserUUID == userUuid.Value);
        if (user is null)
            return this.ToActionResult(OperationResult.Fail("Unauthorized", "Tai khoan khong ton tai."));

        var existing = await _dbContext.DeviceTokens.FirstOrDefaultAsync(t => t.Token == token && t.UserID == user.UserID);
        if (existing is not null)
        {
            existing.IsActive = false;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Da vo hieu hoa FCM token cho user {UserId}", user.UserID);
        }

        return Ok(OperationResult.Ok("Da huy dang ky device token."));
    }
}
