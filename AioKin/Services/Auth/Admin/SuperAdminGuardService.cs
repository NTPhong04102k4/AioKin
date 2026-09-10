using AioKin.Common;
using AioKin.Data;
using AioKin.Models.InputModel.Auth.User;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Auth.Admin;

public class SuperAdminGuardService : ISuperAdminGuardService
{
    private readonly AioKinDbContext _db;

    public SuperAdminGuardService(AioKinDbContext db)
    {
        _db = db;
    }

    public async Task<OperationResult?> RejectIfSecondSuperAdminAsync(int requestedRoleId, int? excludeStaffId = null)
    {
        var role = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleID == requestedRoleId);
        if (role is null || !string.Equals(role.RoleName, Roles.SUPERADMIN, StringComparison.Ordinal))
            return null;

        var exists = await _db.Staffs.AnyAsync(s =>
            s.RoleID == role.RoleID && (!excludeStaffId.HasValue || s.StaffID != excludeStaffId.Value));

        return exists
            ? OperationResult.Fail("SuperAdminLimit", "Chi duoc phep co mot tai khoan SuperAdmin trong he thong.")
            : null;
    }
}
