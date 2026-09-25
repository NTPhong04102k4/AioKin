using AioKin.Common;
using AioKin.Data;
using AioKin.Models.InputModel.Auth.Admin;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Auth.Admin;
using AioKin.Services.Auth.Admin;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.Token;
using Microsoft.EntityFrameworkCore;
using StaffDb = AioKin.Data.Entities.Security.Staff;

namespace AioKin.Services.Auth.StaffManagement;

public class StaffManagementService : IStaffManagementService
{
    private readonly AioKinDbContext _db;
    private readonly ISuperAdminGuardService _superAdminGuard;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IAccessTokenService _accessTokenService;
    private readonly ILogger<StaffManagementService> _logger;

    public StaffManagementService(
        AioKinDbContext db,
        ISuperAdminGuardService superAdminGuard,
        IRefreshTokenService refreshTokenService,
        IAccessTokenService accessTokenService,
        ILogger<StaffManagementService> logger)
    {
        _db = db;
        _superAdminGuard = superAdminGuard;
        _refreshTokenService = refreshTokenService;
        _accessTokenService = accessTokenService;
        _logger = logger;
    }

    public async Task<StaffListResponse> GetPagedAsync(
        int page, int pageSize, int? roleId = null, int? locationId = null, bool? isActive = null, string? keyword = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.Staffs
            .AsNoTracking()
            .Include(s => s.Role)
            .Include(s => s.Location)
            .AsQueryable();

        if (roleId.HasValue) query = query.Where(s => s.RoleID == roleId.Value);
        if (locationId.HasValue) query = query.Where(s => s.LocationID == locationId.Value);
        if (isActive.HasValue) query = query.Where(s => s.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{keyword.Trim()}%";
            query = query.Where(s =>
                EF.Functions.ILike(s.Username, pattern)
                || EF.Functions.ILike(s.FullName, pattern)
                || EF.Functions.ILike(s.Email, pattern)
                || (s.Phone != null && EF.Functions.ILike(s.Phone, pattern))
                || EF.Functions.ILike(s.StaffCode, pattern));
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(s => s.IsActive)
            .ThenByDescending(s => s.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new StaffListResponse
        {
            Items = rows.Select(Map).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<StaffManagementViewModel?> GetByIdAsync(int staffId)
    {
        var entity = await _db.Staffs
            .AsNoTracking()
            .Include(s => s.Role)
            .Include(s => s.Location)
            .FirstOrDefaultAsync(s => s.StaffID == staffId);

        return entity is null ? null : Map(entity);
    }

    public async Task<OperationResult> UpdateAsync(int staffId, StaffUpdateRequest model)
    {
        var entity = await _db.Staffs.FirstOrDefaultAsync(s => s.StaffID == staffId);
        if (entity is null)
            return OperationResult.Fail("NotFound", "Khong tim thay nhan vien.");

        if (!await _db.Locations.AnyAsync(l => l.LocationID == model.LocationID))
            return OperationResult.Fail("InvalidLocation", "LocationID khong hop le.");

        if (!await _db.Roles.AnyAsync(r => r.RoleID == model.RoleID))
            return OperationResult.Fail("InvalidRole", "RoleID khong hop le.");

        if (await _db.Staffs.AnyAsync(s => s.StaffID != staffId && s.Email == model.Email))
            return OperationResult.Fail("EmailExists", "Email da ton tai.");

        if (!string.IsNullOrWhiteSpace(model.Phone)
            && await _db.Staffs.AnyAsync(s => s.StaffID != staffId && s.Phone == model.Phone))
            return OperationResult.Fail("PhoneExists", "So dien thoai da ton tai.");

        if (model.RoleID != entity.RoleID)
        {
            var superAdminBlock = await _superAdminGuard.RejectIfSecondSuperAdminAsync(model.RoleID, staffId);
            if (superAdminBlock is not null)
                return superAdminBlock;
        }

        entity.FullName = model.FullName.Trim();
        entity.Email = model.Email.Trim();
        entity.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
        entity.LocationID = model.LocationID;
        entity.RoleID = model.RoleID;

        await _db.SaveChangesAsync();

        return OperationResult.Ok("Cap nhat nhan vien thanh cong.");
    }

    public async Task<OperationResult> ChangeStatusAsync(int staffId, StaffStatusRequest model, string callerUsername)
    {
        var entity = await _db.Staffs.Include(s => s.Role).FirstOrDefaultAsync(s => s.StaffID == staffId);
        if (entity is null)
            return OperationResult.Fail("NotFound", "Khong tim thay nhan vien.");

        var guard = GuardAgainstDisablingLastSuperAdmin(entity, model.IsActive, callerUsername);
        if (guard is not null)
            return guard;

        entity.IsActive = model.IsActive;
        await _db.SaveChangesAsync();

        if (!model.IsActive)
        {
            await _refreshTokenService.RevokeAllAsync(entity.Username);
            await _accessTokenService.RevokeAllForSubjectAsync(entity.Username);
        }

        _logger.LogInformation("Staff {StaffId} status set to {IsActive} by {Caller}", staffId, model.IsActive, callerUsername);
        return OperationResult.Ok(model.IsActive ? "Da mo khoa nhan vien." : "Da khoa nhan vien.");
    }

    public async Task<OperationResult> DeleteAsync(int staffId, string callerUsername)
    {
        var entity = await _db.Staffs.Include(s => s.Role).FirstOrDefaultAsync(s => s.StaffID == staffId);
        if (entity is null)
            return OperationResult.Fail("NotFound", "Khong tim thay nhan vien.");

        var guard = GuardAgainstDisablingLastSuperAdmin(entity, activating: false, callerUsername);
        if (guard is not null)
            return guard;

        // Xoa mem: ban ghi Staff con duoc tham chieu qua CreatedBy tren cac nhan vien khac,
        // xoa cung se lam mat vet nguoi tao.
        entity.IsActive = false;
        await _db.SaveChangesAsync();

        await _refreshTokenService.RevokeAllAsync(entity.Username);
        await _accessTokenService.RevokeAllForSubjectAsync(entity.Username);

        _logger.LogInformation("Staff {StaffId} deactivated by {Caller}", staffId, callerUsername);
        return OperationResult.Ok("Da vo hieu hoa nhan vien.");
    }

    /// <summary>
    /// Chan viec tat tai khoan SuperAdmin duy nhat — ke ca chinh no tu tat. Neu cho phep,
    /// he thong mat vinh vien duong vao quyen cao nhat va chi con cach sua tay database.
    /// </summary>
    private static OperationResult? GuardAgainstDisablingLastSuperAdmin(StaffDb entity, bool activating, string callerUsername)
    {
        if (activating)
            return null;

        if (!string.Equals(entity.Role?.RoleName, Roles.SUPERADMIN, StringComparison.Ordinal))
            return null;

        return OperationResult.Fail("Forbidden",
            "Khong the vo hieu hoa tai khoan SuperAdmin duy nhat cua he thong.");
    }

    private static StaffManagementViewModel Map(StaffDb s) => new()
    {
        StaffID = s.StaffID,
        StaffCode = s.StaffCode,
        Username = s.Username,
        FullName = s.FullName,
        Email = s.Email,
        Phone = s.Phone,
        LocationID = s.LocationID,
        LocationName = s.Location?.LocationName,
        RoleID = s.RoleID,
        RoleName = s.Role?.RoleName,
        IsActive = s.IsActive,
        CreatedBy = s.CreatedBy,
        CreatedDate = s.CreatedDate
    };
}
