using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Vault;
using AioKin.Models.ViewModel.Vault;

namespace AioKin.Services.Vault;

public interface ISpaceService
{
    /// <summary>Tra ve Space personal cua caller, tao moi neu chua co. Idempotent.</summary>
    Task<SpaceResponse> EnsureMyPersonalSpaceAsync(Guid callerUserUuid, CancellationToken cancellationToken = default);

    /// <summary>Tao Team space moi. Nguoi tao thanh Owner trong cung transaction.</summary>
    Task<OperationResult> CreateTeamAsync(Guid callerUserUuid, CreateTeamSpaceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Them thanh vien vao Team. Danh tinh nguoi goi LUON lay tu token qua ISpaceContext —
    /// khong nhan callerUserUuid lam tham so de tranh gia mao. Chi nguoi co CanManage (Owner/
    /// Admin cua Team, hoac tuong duong o Family/Personal) moi goi duoc; nguoc lai tra ve
    /// OperationResult.Fail("Forbidden", ...) thay vi nem loi.
    /// </summary>
    Task<OperationResult> AddMemberAsync(Guid spaceUuid, AddSpaceMemberRequest request, CancellationToken cancellationToken = default);

    /// <summary>Moi space nguoi goi thuoc ve: personal (tu tao neu chua co) + family + team.</summary>
    Task<IReadOnlyList<SpaceResponse>> GetMineAsync(Guid callerUserUuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Danh sach thanh vien cua Team. Bat ky thanh vien nao (Owner/Admin/Member) deu xem duoc —
    /// khong can CanManage, chi can resolve duoc tu cach thanh vien. Danh tinh nguoi goi LUON
    /// lay tu token qua ISpaceContext, giong AddMemberAsync (D1) — khong nhan callerUserUuid.
    /// </summary>
    Task<OperationResult> ListMembersAsync(Guid spaceUuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xoa mot thanh vien khoi Team. Tu xoa chinh minh ("roi team") luon duoc phep bat ke
    /// CanManage; xoa nguoi khac can CanManage (Owner/Admin cua chinh team nay). Danh tinh
    /// nguoi goi LUON lay tu token qua ISpaceContext (D1), khong nhan callerUserUuid.
    /// </summary>
    Task<OperationResult> RemoveMemberAsync(Guid spaceUuid, Guid targetUserUuid, CancellationToken cancellationToken = default);
}
