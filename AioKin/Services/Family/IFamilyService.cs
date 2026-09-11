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
}
