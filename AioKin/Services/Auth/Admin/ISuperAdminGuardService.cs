using AioKin.Models.InputModel.Auth.User;

namespace AioKin.Services.Auth.Admin;

/// <summary>Giu rang buoc: he thong chi co dung mot tai khoan Staff mang role SuperAdmin.</summary>
public interface ISuperAdminGuardService
{
    /// <summary>
    /// Tra ve <see cref="OperationResult"/> loi neu dang tao/gan them SuperAdmin trong khi
    /// da co mot tai khoan nhu vay; nguoc lai tra null.
    /// </summary>
    /// <param name="requestedRoleId">RoleID dang duoc gan cho nhan vien.</param>
    /// <param name="excludeStaffId">Staff dang duoc sua — bo qua chinh no khi dem.</param>
    Task<OperationResult?> RejectIfSecondSuperAdminAsync(int requestedRoleId, int? excludeStaffId = null);
}
