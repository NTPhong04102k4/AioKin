using AioKin.Common;
using AioKin.Data;
using AioKin.Data.Entities.Security;
using AioKin.Models.InputModel.Auth.Admin;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Auth.Admin;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.Token;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Auth.Admin;

public class AdminAuthService : IAdminAuthService
{
    private readonly AioKinDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ISuperAdminGuardService _superAdminGuard;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ILogger<AdminAuthService> _logger;

    public AdminAuthService(
        AioKinDbContext db,
        IConfiguration configuration,
        ISuperAdminGuardService superAdminGuard,
        IAccessTokenService accessTokenService,
        IRefreshTokenService refreshTokenService,
        ILogger<AdminAuthService> logger)
    {
        _db = db;
        _configuration = configuration;
        _superAdminGuard = superAdminGuard;
        _accessTokenService = accessTokenService;
        _refreshTokenService = refreshTokenService;
        _logger = logger;
    }

    // ─── Dang nhap ────────────────────────────────────────────────────────────

    public async Task<LoginAdminResponse?> LoginAsync(LoginAdminRequest request)
    {
        var staff = await _db.Staffs
            .AsNoTracking()
            .Include(s => s.Role)
            .FirstOrDefaultAsync(s => s.Username == request.Username);

        // Tra null chung cho ca "khong ton tai", "sai mat khau" va "bi tat" — phan biet
        // cac truong hop nay cho phep do xem username nao co that trong he thong.
        if (staff is null)
            return null;

        if (!PasswordHelper.VerifyPassword(request.Password, staff.PasswordHash, staff.PasswordSalt))
        {
            _logger.LogWarning("Admin login failed for username={Username}", request.Username);
            return null;
        }

        if (!staff.IsActive)
        {
            _logger.LogWarning("Admin login blocked, account disabled: username={Username}", request.Username);
            return null;
        }

        var roleName = staff.Role?.RoleName ?? Roles.STAFF;

        // Dang nhap admin khong co truong thiet bi tren DTO (chua trong pham vi ke hoach nay) —
        // van sinh mot dinh danh thiet bi server-side de phien luon co the truy va thu hoi rieng.
        var device = DeviceInfo.Resolve(null, null, null);

        return new LoginAdminResponse
        {
            FullName = staff.FullName,
            Username = staff.Username,
            Role = roleName,
            Token = await _accessTokenService.CreateForStaffAsync(staff, roleName, device),
            RefreshToken = await _refreshTokenService.GenerateAsync(staff.Username, roleName, device),
            ExpiresIn = _accessTokenService.AccessTokenLifetimeSeconds
        };
    }

    // ─── Tao nhan vien ────────────────────────────────────────────────────────

    public async Task<OperationResult> CreateStaffAsync(CreateStaffRequest request, int createdByStaffId)
    {
        if (await _db.Staffs.AnyAsync(s => s.Username == request.Username))
            return OperationResult.Fail("UsernameExists", "Username da ton tai.");

        if (await _db.Staffs.AnyAsync(s => s.Email == request.Email))
            return OperationResult.Fail("EmailExists", "Email da ton tai.");

        if (!string.IsNullOrWhiteSpace(request.Phone) && await _db.Staffs.AnyAsync(s => s.Phone == request.Phone))
            return OperationResult.Fail("PhoneExists", "So dien thoai da ton tai.");

        var location = await _db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.LocationID == request.LocationID);
        if (location is null)
            return OperationResult.Fail("InvalidLocation", "LocationID khong hop le.");

        var role = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleID == request.RoleID);
        if (role is null)
            return OperationResult.Fail("InvalidRole", "RoleID khong hop le.");

        var superAdminBlock = await _superAdminGuard.RejectIfSecondSuperAdminAsync(request.RoleID);
        if (superAdminBlock is not null)
            return superAdminBlock;

        PasswordHelper.CreatePasswordHash(request.Password, out var hash, out var salt);

        var staff = new Staff
        {
            Username = request.Username,
            FullName = request.FullName,
            Email = request.Email,
            PasswordHash = hash,
            PasswordSalt = salt,
            Phone = request.Phone,
            LocationID = request.LocationID,
            RoleID = request.RoleID,
            IsActive = true,
            CreatedBy = createdByStaffId,
            StaffCode = Utils.HashTo20Chars(
                $"STF-{location.LocationName}-L:{request.LocationID}-R:{request.RoleID}-U:{request.Username}")
        };

        _db.Staffs.Add(staff);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Staff created: username={Username}, role={Role}, by={CreatedBy}",
            staff.Username, role.RoleName, createdByStaffId);

        return OperationResult.Ok("Tao nhan vien thanh cong.", new StaffResponse
        {
            StaffCode = staff.StaffCode,
            Username = staff.Username,
            FullName = staff.FullName,
            Email = staff.Email,
            Phone = staff.Phone,
            Role = role.RoleName,
            Location = location.LocationName
        });
    }

    // ─── Doi mat khau ─────────────────────────────────────────────────────────

    public async Task<OperationResult> UpdateStaffPasswordAsync(
        int staffId, UpdateStaffPasswordRequest request, string callerUsername)
    {
        var caller = await _db.Staffs.AsNoTracking().Include(s => s.Role)
            .FirstOrDefaultAsync(s => s.Username == callerUsername);

        if (caller is null)
            return OperationResult.Fail("Unauthorized", "Khong xac dinh duoc tai khoan dang nhap.");

        var isSuperAdmin = string.Equals(caller.Role?.RoleName, Roles.SUPERADMIN, StringComparison.Ordinal);
        var isSelf = caller.StaffID == staffId;

        if (!isSuperAdmin && !isSelf)
            return OperationResult.Fail("Forbidden", "Ban chi co the doi mat khau cua chinh minh.");

        var target = await _db.Staffs.FirstOrDefaultAsync(s => s.StaffID == staffId);
        if (target is null)
            return OperationResult.Fail("NotFound", "Khong tim thay nhan vien.");

        // SuperAdmin dat lai mat khau cho nguoi khac thi khong the biet mat khau cu.
        // Moi truong hop con lai — ke ca SuperAdmin doi cho chinh minh — deu phai xac
        // nhan mat khau hien tai, de mot phien bi chiem khong doi duoc mat khau.
        if (!(isSuperAdmin && !isSelf))
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                return OperationResult.Fail("ValidationError", "Vui long nhap mat khau hien tai.");

            if (!PasswordHelper.VerifyPassword(request.CurrentPassword, target.PasswordHash, target.PasswordSalt))
                return OperationResult.Fail("InvalidCredentials", "Mat khau hien tai khong dung.");
        }

        PasswordHelper.CreatePasswordHash(request.NewPassword, out var hash, out var salt);
        target.PasswordHash = hash;
        target.PasswordSalt = salt;
        await _db.SaveChangesAsync();

        // Moi phien cu phai chet theo mat khau cu, khong thi doi mat khau khong duoi duoc
        // ke dang giu refresh token.
        await _refreshTokenService.RevokeAllAsync(target.Username);
        await _accessTokenService.RevokeAllForSubjectAsync(target.Username);

        _logger.LogInformation("Staff password updated: staffId={StaffId}, by={Caller}", staffId, callerUsername);
        return OperationResult.Ok("Doi mat khau thanh cong.");
    }

    // ─── Khoi phuc SuperAdmin ─────────────────────────────────────────────────

    public async Task<OperationResult> RecoverSuperAdminPasswordAsync(SuperAdminRecoverPasswordRequest request)
    {
        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
            return OperationResult.Fail("ValidationError", "Mat khau moi va xac nhan khong khop.");

        var configuredCode = _configuration["Auth:SuperAdminRecoveryCode"];

        // Khong co gia tri mac dinh trong code: mot ma khoi phuc hardcode chinh la cua hau
        // vao tai khoan quyen cao nhat cho bat ky ai doc duoc repo. Chua cau hinh thi tinh
        // nang tat han.
        if (string.IsNullOrWhiteSpace(configuredCode))
        {
            _logger.LogWarning("SuperAdmin recovery attempted but Auth:SuperAdminRecoveryCode is not configured.");
            return OperationResult.Fail("Forbidden", "Tinh nang khoi phuc chua duoc cau hinh tren he thong nay.");
        }

        if (!string.Equals(request.RecoveryCode.Trim(), configuredCode, StringComparison.Ordinal))
        {
            _logger.LogWarning("SuperAdmin recovery failed: wrong recovery code.");
            return OperationResult.Fail("InvalidRecoveryCode", "Ma khoi phuc khong dung.");
        }

        var superRole = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleName == Roles.SUPERADMIN);
        if (superRole is null)
            return OperationResult.Fail("NotFound", "Khong tim thay role SuperAdmin trong he thong.");

        var superAdmins = await _db.Staffs.Where(s => s.RoleID == superRole.RoleID).ToListAsync();

        if (superAdmins.Count == 0)
            return OperationResult.Fail("NotFound", "Chua co tai khoan SuperAdmin de khoi phuc.");

        if (superAdmins.Count > 1)
            return OperationResult.Fail("SuperAdminAmbiguous",
                "Du lieu khong hop le: co nhieu hon mot tai khoan SuperAdmin. Vui long xu ly thu cong tren database.");

        var target = superAdmins[0];
        PasswordHelper.CreatePasswordHash(request.NewPassword, out var hash, out var salt);
        target.PasswordHash = hash;
        target.PasswordSalt = salt;
        target.IsActive = true;
        await _db.SaveChangesAsync();

        await _refreshTokenService.RevokeAllAsync(target.Username);
        await _accessTokenService.RevokeAllForSubjectAsync(target.Username);

        _logger.LogWarning("SuperAdmin password recovered for username={Username}", target.Username);
        return OperationResult.Ok("Da dat lai mat khau SuperAdmin. Vui long dang nhap lai.",
            new { username = target.Username });
    }
}
