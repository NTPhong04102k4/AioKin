using AioKin.Common;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Family;
using AioKin.Models.ViewModel.Family;
using AioKin.Services.Family;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Family;

/// <summary>
/// Ho gia dinh — pham vi chia se ma chat nhom, kho file va so chi tieu deu dua tren.
///
/// Danh tinh LUON lay tu token. Khong endpoint nao o day nhan mot tham so noi nguoi goi la
/// ai; tham so <c>uuid</c> tren duong dan la gia dinh dang thao tac, va no luon di qua
/// <see cref="IFamilyContext"/> truoc khi cham du lieu.
/// </summary>
[ApiController]
[Route("families")]
[Produces("application/json")]
[Authorize(Roles = Roles.CUSTOMER)]
public class FamiliesController : ControllerBase
{
    private readonly IFamilyService _familyService;

    public FamiliesController(IFamilyService familyService)
    {
        _familyService = familyService;
    }

    /// <summary>Tao gia dinh moi. Nguoi tao tro thanh Owner.</summary>
    [HttpPost]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateFamilyRequest request,
        CancellationToken cancellationToken)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return this.ToActionResult(
                OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        return this.ToActionResult(
            await _familyService.CreateAsync(userUuid.Value, request, cancellationToken));
    }

    /// <summary>Cac gia dinh nguoi goi dang thuoc ve.</summary>
    [HttpGet("me")]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return this.ToActionResult(
                OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        var families = await _familyService.GetMineAsync(userUuid.Value, cancellationToken);

        return this.ToActionResult(OperationResult.Ok(data: families));
    }

    /// <summary>Tao ma moi vao gia dinh. Owner va Adult goi duoc.</summary>
    [HttpPost("{uuid:guid}/invites")]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateInvite(
        Guid uuid,
        [FromBody] CreateInviteRequest request,
        CancellationToken cancellationToken)
        => this.ToActionResult(await _familyService.CreateInviteAsync(uuid, request, cancellationToken));

    /// <summary>Vao mot gia dinh bang ma moi.</summary>
    [HttpPost("join")]
    [ProducesResponseType<OperationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Join(
        [FromBody] JoinFamilyRequest request,
        CancellationToken cancellationToken)
    {
        var userUuid = User.GetUserUuid();
        if (userUuid is null)
            return this.ToActionResult(
                OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        return this.ToActionResult(await _familyService.JoinAsync(userUuid.Value, request, cancellationToken));
    }
}
