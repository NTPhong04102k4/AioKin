using AioKin.Common;
using AioKin.Data.Entities.Core;
using AioKin.Data.Entities.Security;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Data;

/// <summary>
/// Dua database ve trang thai toi thieu de luong auth chay duoc: bon role, mot location
/// mac dinh, va tai khoan SuperAdmin dau tien. Khong co nhung ban ghi nay thi
/// <c>POST /auth/admin/staff/create</c> khong bao gio goi duoc (can RoleID + LocationID
/// hop le) va cung khong ai dang nhap duoc de goi no — bai toan con ga qua trung.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AioKinDbContext db, IConfiguration config, ILogger logger)
    {
        await SeedRolesAsync(db, logger);
        await SeedDefaultLocationAsync(db, logger);
        await SeedSuperAdminAsync(db, config, logger);
    }

    private static async Task SeedRolesAsync(AioKinDbContext db, ILogger logger)
    {
        var wanted = new (string Name, string Description)[]
        {
            (Roles.SUPERADMIN, "Toan quyen he thong. Chi ton tai duy nhat mot tai khoan."),
            (Roles.ADMIN, "Quan tri nguoi dung va nhan vien."),
            (Roles.STAFF, "Nhan vien nghiep vu."),
            (Roles.CUSTOMER, "Khach hang — dung cho phan quyen, khong gan vao bang Staff.")
        };

        var existing = await db.Roles.Select(r => r.RoleName).ToListAsync();
        var missing = wanted.Where(w => !existing.Contains(w.Name, StringComparer.Ordinal)).ToList();
        if (missing.Count == 0)
            return;

        db.Roles.AddRange(missing.Select(m => new Role { RoleName = m.Name, Description = m.Description }));
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} role(s): {Roles}", missing.Count, string.Join(", ", missing.Select(m => m.Name)));
    }

    private static async Task SeedDefaultLocationAsync(AioKinDbContext db, ILogger logger)
    {
        if (await db.Locations.AnyAsync())
            return;

        db.Locations.Add(new Location
        {
            LocationCode = "HQ",
            LocationName = "Head Office",
            LocationType = "Office",
            IsActive = true
        });
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded default location HQ.");
    }

    /// <summary>
    /// Tao SuperAdmin dau tien tu <c>Auth:SuperAdmin:*</c>. Khong co mat khau trong config
    /// thi bo qua thay vi dat mat khau mac dinh — mot mat khau mac dinh trong source code
    /// la tai khoan quan tri cong khai cho bat ky ai doc duoc repo.
    /// </summary>
    private static async Task SeedSuperAdminAsync(AioKinDbContext db, IConfiguration config, ILogger logger)
    {
        var superRole = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == Roles.SUPERADMIN);
        if (superRole is null)
            return;

        if (await db.Staffs.AnyAsync(s => s.RoleID == superRole.RoleID))
            return;

        var username = config["Auth:SuperAdmin:Username"];
        var email = config["Auth:SuperAdmin:Email"];
        var password = config["Auth:SuperAdmin:Password"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Chua co SuperAdmin va cung chua cau hinh Auth:SuperAdmin:{{Username,Email,Password}} — bo qua seed. "
                + "Dat ba gia tri nay (user-secrets hoac bien moi truong) roi chay lai de tao tai khoan quan tri dau tien.");
            return;
        }

        var location = await db.Locations.OrderBy(l => l.LocationID).FirstAsync();
        PasswordHelper.CreatePasswordHash(password, out var hash, out var salt);

        db.Staffs.Add(new Staff
        {
            Username = username,
            Email = email,
            FullName = config["Auth:SuperAdmin:FullName"] ?? "Super Administrator",
            PasswordHash = hash,
            PasswordSalt = salt,
            LocationID = location.LocationID,
            RoleID = superRole.RoleID,
            IsActive = true,
            CreatedBy = 0,
            StaffCode = Utils.HashTo20Chars($"STF-{location.LocationName}-L:{location.LocationID}-R:{superRole.RoleID}-U:{username}")
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded SuperAdmin account {Username}.", username);
    }
}
