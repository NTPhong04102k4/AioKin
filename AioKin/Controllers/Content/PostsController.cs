using AioKin.Common;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Content;
using AioKin.Services.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Content;

/// <summary>
/// Noi dung man Kham pha.
///
/// Route la <c>/posts</c> chu khong phai <c>/discovery</c> vi do la duong dan
/// <c>ApiService</c> ben Android dang goi (di tich tu thoi tro tam sang jsonplaceholder).
/// Doi route la app 404 ngay, nen sua thi phai sua ca hai dau cung luc.
///
/// KHONG boc <c>OperationResult</c> o duong thanh cong: DTO ben app parse thang mot mang
/// JSON. Loi thi van tra <c>OperationResult</c> nhu phan con lai cua API — Retrofit nem
/// HttpException truoc khi parse body voi moi status khong phai 2xx, nen khong dung nham
/// kieu duoc.
///
/// <see cref="AllowAnonymousAttribute"/> la co y: day la noi dung cong khai, khong co du
/// lieu cua ai ca. Lich trinh — thu that su rieng tu — nam o <see cref="TodosController"/>
/// va co yeu cau token.
/// </summary>
[ApiController]
[Route("posts")]
[Produces("application/json")]
[AllowAnonymous]
public class PostsController : ControllerBase
{
    private readonly IDiscoveryService _discoveryService;

    public PostsController(IDiscoveryService discoveryService)
    {
        _discoveryService = discoveryService;
    }

    /// <summary>Toan bo the Kham pha da publish.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DiscoveryItemResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        => Ok(await _discoveryService.GetAllAsync(cancellationToken));

    /// <summary>Chi tiet mot the.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<DiscoveryItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<OperationResult>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var item = await _discoveryService.GetByIdAsync(id, cancellationToken);

        return item is null
            ? this.ToActionResult(OperationResult.Fail("NotFound", "Khong tim thay noi dung."))
            : Ok(item);
    }
}
