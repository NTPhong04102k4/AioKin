using AioKin.Common;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Vault;
using AioKin.Services.Vault;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Vault;

/// <summary>
/// Danh tinh LUON lay tu token. spaceUuid/userUuid tren duong dan la doi tuong dang thao tac,
/// khong phai danh tinh nguoi goi.
/// </summary>
[ApiController]
[Route("spaces")]
[Produces("application/json")]
[Authorize(Roles = Roles.CUSTOMER)]
public class SpacesController : ControllerBase
{
    private readonly ISpaceService _spaceService;

    public SpacesController(ISpaceService spaceService)
    {
        _spaceService = spaceService;
    }

    /// <summary>Moi space nguoi goi thuoc ve — personal (tu tao neu chua co), family, team.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return this.ToActionResult(OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        return this.ToActionResult(OperationResult.Ok(data: await _spaceService.GetMineAsync(userUuid.Value, cancellationToken)));
    }

    /// <summary>Tao Team space moi. Nguoi tao thanh Owner.</summary>
    [HttpPost("team")]
    public async Task<IActionResult> CreateTeam([FromBody] CreateTeamSpaceRequest request, CancellationToken cancellationToken)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return this.ToActionResult(OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        return this.ToActionResult(await _spaceService.CreateTeamAsync(userUuid.Value, request, cancellationToken));
    }

    /// <summary>Them thanh vien vao Team bang UserCode. Chi Owner/Admin cua team nay goi duoc.</summary>
    [HttpPost("{uuid:guid}/members")]
    public async Task<IActionResult> AddMember(Guid uuid, [FromBody] AddSpaceMemberRequest request, CancellationToken cancellationToken)
        => this.ToActionResult(await _spaceService.AddMemberAsync(uuid, request, cancellationToken));

    /// <summary>Danh sach thanh vien cua Team. Bat ky thanh vien nao (Owner/Admin/Member) deu xem duoc.</summary>
    [HttpGet("{uuid:guid}/members")]
    public async Task<IActionResult> ListMembers(Guid uuid, CancellationToken cancellationToken)
        => this.ToActionResult(await _spaceService.ListMembersAsync(uuid, cancellationToken));

    /// <summary>
    /// Xoa mot thanh vien khoi Team. Tu xoa chinh minh ("roi team") luon duoc phep; xoa nguoi
    /// khac can Owner/Admin cua chinh team nay.
    /// </summary>
    [HttpDelete("{uuid:guid}/members/{userUuid:guid}")]
    public async Task<IActionResult> RemoveMember(Guid uuid, Guid userUuid, CancellationToken cancellationToken)
        => this.ToActionResult(await _spaceService.RemoveMemberAsync(uuid, userUuid, cancellationToken));
}
