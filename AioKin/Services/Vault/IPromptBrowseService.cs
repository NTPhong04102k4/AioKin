using AioKin.Models.InputModel.Auth.User;

namespace AioKin.Services.Vault;

/// <summary>
/// Chi doc: liet ke/xem chi tiet Prompt, va liet ke Category/Tag cua mot Space. KHONG co
/// tao/sua/xoa o day — 4 entity Prompt/Category/Tag/PromptVariable chi ghi qua sync engine
/// (/sync/push, plan rieng) de giu version tracking va conflict detection.
///
/// Danh tinh nguoi goi LUON lay tu token qua ISpaceContext.ResolveAsync — khong nhan
/// callerUserUuid lam tham so, giong SpaceService (D1/AddMemberAsync).
/// </summary>
public interface IPromptBrowseService
{
    Task<OperationResult> ListAsync(Guid spaceUuid, CancellationToken cancellationToken = default);

    Task<OperationResult> GetAsync(Guid spaceUuid, Guid promptId, CancellationToken cancellationToken = default);

    Task<OperationResult> ListCategoriesAsync(Guid spaceUuid, CancellationToken cancellationToken = default);

    Task<OperationResult> ListTagsAsync(Guid spaceUuid, CancellationToken cancellationToken = default);
}
