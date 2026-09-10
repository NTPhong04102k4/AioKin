using AioKin.Common;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Ability;
using AioKin.Services.Auth.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Content;

/// <summary>
/// Bo rule phan quyen cua chinh nguoi dang dang nhap.
///
/// Mot bo rule dung chung cho ca web (CASL that) lan app Android (ban cai lai ngu nghia
/// trong <c>data/ability/</c>), nen backend la noi duy nhat quyet dinh ai lam duoc gi.
///
/// Role LUON doc tu claim trong token, khong bao gio tu tham so: nhan ten role tu client
/// nghia la ai cung tu cap cho minh bo rule cua SuperAdmin bang mot chuoi query.
///
/// Khong boc <c>OperationResult</c> o duong thanh cong — mang JSON tran, cung ly do voi
/// <see cref="PostsController"/>. Va THU TU PHAN TU LA NGU NGHIA: xem
/// <see cref="AbilityRuleResponse"/>, khong tang nao duoc phep sap xep lai.
/// </summary>
[ApiController]
[Route("ability")]
[Produces("application/json")]
[Authorize]
public class AbilityController : ControllerBase
{
    private readonly IPermissionService _permissionService;

    public AbilityController(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    /// <summary>Rule cua role trong token, giu nguyen thu tu da luu trong database.</summary>
    [HttpGet("rules")]
    [ProducesResponseType<IReadOnlyList<AbilityRuleResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetRules(CancellationToken cancellationToken)
    {
        var role = User.GetRole();

        // Token khong mang role thi khong suy ra duoc bo rule nao. Tra mang rong o day se
        // bi ben app hieu la "da biet chac: cam tat ca", trong khi that ra la chua biet gi —
        // hai trang thai khac nhau, va lan lon chung thi loi cau hinh token se hien ra thanh
        // mot man hinh trong khong ai giai thich duoc.
        if (string.IsNullOrWhiteSpace(role))
            return this.ToActionResult(
                OperationResult.Fail("Unauthorized", "Token thieu thong tin role."));

        return Ok(await _permissionService.GetRulesForRoleAsync(role, cancellationToken));
    }
}
