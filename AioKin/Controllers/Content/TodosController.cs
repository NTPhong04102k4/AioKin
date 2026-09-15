using AioKin.Common;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Content;
using AioKin.Services.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Content;

/// <summary>
/// Lich trinh cua chinh nguoi dang dang nhap.
///
/// Route la <c>/todos</c> chu khong phai <c>/schedule</c> vi do la duong dan
/// <c>ApiService.getScheduleItems()</c> ben Android dang goi — cung di tich jsonplaceholder
/// nhu <see cref="PostsController"/>. Doi route thi phai doi ca hai dau cung luc.
///
/// Khac <see cref="PostsController"/> o mot diem quan trong: day la du lieu rieng tu, nen
/// endpoint yeu cau token va danh tinh LUON lay tu token. Khong co tham so userId nao ca —
/// nhan id tu client nghia la doi mot con so la doc duoc lich cua nguoi khac.
///
/// Duong thanh cong khong boc <c>OperationResult</c> (mang JSON tran cho Gson), duong loi
/// thi van boc nhu phan con lai cua API.
/// </summary>
[ApiController]
[Route("todos")]
[Produces("application/json")]
[Authorize(Roles = Roles.CUSTOMER)]
public class TodosController : ControllerBase
{
    private readonly IScheduleService _scheduleService;

    public TodosController(IScheduleService scheduleService)
    {
        _scheduleService = scheduleService;
    }

    /// <summary>Lich trinh cua tai khoan dang dang nhap, sap theo thoi diem bat dau tang dan.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ScheduleItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var userUuid = User.GetUserUuid();

        // Token hop le nhung thieu "sub" — khong the biet lich cua ai. Tra rong o day se la
        // noi doi: "ban khong co muc nao" trong khi that ra la khong doc duoc danh tinh.
        if (userUuid is null)
            return this.ToActionResult(
                OperationResult.Fail("Unauthorized", "Token thieu thong tin nguoi dung."));

        return Ok(await _scheduleService.GetForUserAsync(userUuid.Value, cancellationToken));
    }
}
