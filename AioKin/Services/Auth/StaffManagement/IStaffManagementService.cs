using AioKin.Models.InputModel.Auth.Admin;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Auth.Admin;

namespace AioKin.Services.Auth.StaffManagement;

/// <summary>CRUD nhan vien phia quan tri (khac voi luong xac thuc trong IAdminAuthService).</summary>
public interface IStaffManagementService
{
    Task<StaffListResponse> GetPagedAsync(
        int page,
        int pageSize,
        int? roleId = null,
        int? locationId = null,
        bool? isActive = null,
        string? keyword = null);

    Task<StaffManagementViewModel?> GetByIdAsync(int staffId);

    Task<OperationResult> UpdateAsync(int staffId, StaffUpdateRequest model);

    Task<OperationResult> ChangeStatusAsync(int staffId, StaffStatusRequest model, string callerUsername);

    /// <summary>Xoa mem: dat IsActive = false va thu hoi moi phien dang mo.</summary>
    Task<OperationResult> DeleteAsync(int staffId, string callerUsername);
}
