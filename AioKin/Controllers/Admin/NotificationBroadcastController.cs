using AioKin.Common;
using AioKin.Models.InputModel.Admin;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Services.Common.Notification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Admin;

/// <summary>
/// Quan tri phat song thong bao he thong (FCM broadcast) toi toan bo thiet bi nguoi dung.
/// </summary>
[ApiController]
[Route("admin/notifications")]
[Produces("application/json")]
[Authorize(Roles = $"{Roles.STAFF},{Roles.ADMIN},{Roles.SUPERADMIN}")]
public class NotificationBroadcastController(
    IFcmNotificationService fcmService,
    ILogger<NotificationBroadcastController> logger) : ControllerBase
{
    private readonly IFcmNotificationService _fcmService = fcmService;
    private readonly ILogger<NotificationBroadcastController> _logger = logger;

    /// <summary>Phat thong bao phien ban moi toi toan bo nguoi dung qua topic 'app-updates'.</summary>
    [HttpPost("broadcast-update")]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BroadcastUpdate([FromBody] BroadcastUpdateRequest request, CancellationToken ct)
    {
        var data = new Dictionary<string, string>
        {
            ["type"] = "app_update",
            ["version"] = request.Version
        };

        if (!string.IsNullOrWhiteSpace(request.ChangelogUrl))
        {
            data["changelogUrl"] = request.ChangelogUrl;
        }

        var success = await _fcmService.SendToTopicAsync(
            "app-updates",
            request.Title,
            request.Body,
            data,
            ct);

        _logger.LogInformation("Admin da phat song phien ban moi {Version} toi topic app-updates (Success={Success})",
            request.Version, success);

        return Ok(OperationResult.Ok($"Phat song thong bao phien ban {request.Version} thanh cong."));
    }
}
