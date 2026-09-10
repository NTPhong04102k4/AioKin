using AioKin.Models.InputModel.Auth.Admin;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Auth.Admin;

namespace AioKin.Services.Auth.Admin;

/// <summary>Luong xac thuc phia quan tri: dang nhap Staff, tao Staff, doi/khoi phuc mat khau.</summary>
public interface IAdminAuthService
{
    /// <summary>Dang nhap Staff/Admin/SuperAdmin. Tra null khi sai thong tin hoac tai khoan bi tat.</summary>
    Task<LoginAdminResponse?> LoginAsync(LoginAdminRequest request);

    Task<OperationResult> CreateStaffAsync(CreateStaffRequest request, int createdByStaffId);

    /// <summary>
    /// Staff/Admin doi mat khau cua chinh minh (can mat khau hien tai).
    /// SuperAdmin doi duoc cho bat ky nhan vien nao — doi cho nguoi khac thi khong can
    /// mat khau hien tai, doi cho chinh minh thi van can.
    /// </summary>
    Task<OperationResult> UpdateStaffPasswordAsync(int staffId, UpdateStaffPasswordRequest request, string callerUsername);

    /// <summary>Khoi phuc mat khau SuperAdmin bang ma khoi phuc cau hinh san.</summary>
    Task<OperationResult> RecoverSuperAdminPasswordAsync(SuperAdminRecoverPasswordRequest request);
}
