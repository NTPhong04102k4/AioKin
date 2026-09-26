using AioKin.Common;
using AioKin.Services.Vault;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Controllers.Vault;

/// <summary>
/// Chi doc. Tao/sua/xoa prompt di qua /sync/push (xem
/// docs/superpowers/plans/2026-09-25-promptvault-sync-engine.md) — khong co POST/PUT/DELETE
/// o day, tranh mo mot duong ghi song song bo qua version tracking.
///
/// Danh tinh LUON lay tu token; quyen doc gate qua ISpaceContext.ResolveAsync (thanh vien =
/// doc duoc, khong phai thanh vien = 403 Forbidden, giong het pattern cua SpacesController/
/// SpaceService trong plan nay).
/// </summary>
[ApiController]
[Route("prompts")]
[Produces("application/json")]
[Authorize(Roles = Roles.CUSTOMER)]
public class PromptsController : ControllerBase
{
    private readonly IPromptBrowseService _browseService;
    private readonly IPromptEnrichmentService? _enrichmentService;

    public PromptsController(IPromptBrowseService browseService, IPromptEnrichmentService? enrichmentService = null)
    {
        _browseService = browseService;
        _enrichmentService = enrichmentService;
    }

    /// <summary>Danh sach prompt (chua xoa) trong 1 space, moi nhat truoc.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid spaceUuid, CancellationToken cancellationToken)
        => this.ToActionResult(await _browseService.ListAsync(spaceUuid, cancellationToken));

    /// <summary>Expo gap G5: danh sach category cua 1 space.</summary>
    [HttpGet("categories")]
    public async Task<IActionResult> ListCategories([FromQuery] Guid spaceUuid, CancellationToken cancellationToken)
        => this.ToActionResult(await _browseService.ListCategoriesAsync(spaceUuid, cancellationToken));

    /// <summary>Expo gap G5: danh sach tag cua 1 space.</summary>
    [HttpGet("tags")]
    public async Task<IActionResult> ListTags([FromQuery] Guid spaceUuid, CancellationToken cancellationToken)
        => this.ToActionResult(await _browseService.ListTagsAsync(spaceUuid, cancellationToken));

    /// <summary>Chi tiet 1 prompt (bao gom variables + tags).</summary>
    [HttpGet("{promptId:guid}")]
    public async Task<IActionResult> Get([FromQuery] Guid spaceUuid, Guid promptId, CancellationToken cancellationToken)
        => this.ToActionResult(await _browseService.GetAsync(spaceUuid, promptId, cancellationToken));

    /// <summary>Yeu cau AI cai thien prompt trong background va gui FCM thong bao khi hoan tat.</summary>
    [HttpPost("{promptId:guid}/enrich")]
    public async Task<IActionResult> Enrich([FromQuery] Guid spaceUuid, Guid promptId, CancellationToken cancellationToken)
    {
        if (_enrichmentService is null)
            return StatusCode(StatusCodes.Status501NotImplemented, "Dich vu AI prompt enrichment chua duoc dang ky.");

        return this.ToActionResult(await _enrichmentService.EnrichPromptAsync(spaceUuid, promptId, cancellationToken));
    }
}
