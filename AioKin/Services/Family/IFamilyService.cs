using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Family;
using AioKin.Models.ViewModel.Family;

namespace AioKin.Services.Family;

public interface IFamilyService
{
    /// <summary>Tao gia dinh moi. Nguoi tao thanh Owner trong cung mot transaction.</summary>
    Task<OperationResult> CreateAsync(
        Guid callerUserUuid,
        CreateFamilyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Cac gia dinh nguoi goi dang thuoc ve, moi tao truoc.</summary>
    Task<IReadOnlyList<FamilyResponse>> GetMineAsync(
        Guid callerUserUuid,
        CancellationToken cancellationToken = default);

    /// <summary>Tao ma moi. Chi Owner va Adult goi duoc — Child thi khong.</summary>
    Task<OperationResult> CreateInviteAsync(
        Guid familyUuid,
        CreateInviteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Vao gia dinh bang ma. Vao lai nhom da o trong do la thanh cong va khong ton luot.</summary>
    Task<OperationResult> JoinAsync(
        Guid callerUserUuid,
        JoinFamilyRequest request,
        CancellationToken cancellationToken = default);
}
