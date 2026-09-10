using AioKin.Common;
using AioKin.Data;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.Transfers.ProfileUser;
using AioKin.Models.ViewModel.Auth.User;
using Microsoft.EntityFrameworkCore;
using StaffDb = AioKin.Data.Entities.Security.Staff;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Services.Auth.User;

public class UserService : IUserService
{
    private readonly AioKinDbContext _db;
    private readonly ILogger<UserService> _logger;

    public UserService(AioKinDbContext db, ILogger<UserService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ─── Tra cuu ──────────────────────────────────────────────────────────────

    public Task<UserDb?> GetByUsernameOrEmailAsync(string identifier)
        => _db.Users.FirstOrDefaultAsync(u =>
            u.Username == identifier || u.Email == identifier || u.Phone == identifier);

    public Task<UserDb?> GetByUserCodeAsync(string userCode)
        => _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserCode == userCode);

    public Task<UserDb?> GetByUuidAsync(Guid userUuid)
        => _db.Users.FirstOrDefaultAsync(u => u.UserUUID == userUuid);

    public Task<UserDb?> GetBySocialIdAsync(string socialId)
        => _db.Users.FirstOrDefaultAsync(u => u.IDSocial == socialId);

    public Task<StaffDb?> GetStaffByUsernameAsync(string username)
        => _db.Staffs.AsNoTracking().Include(s => s.Role).FirstOrDefaultAsync(s => s.Username == username);

    // ─── Ghi ──────────────────────────────────────────────────────────────────

    public async Task<UserDb> CreateUserAsync(UserDb user)
    {
        if (!string.IsNullOrEmpty(user.Email) && await EmailExistsAsync(user.Email))
            throw new InvalidOperationException($"Email '{user.Email}' da ton tai.");

        if (await UsernameExistsAsync(user.Username))
            throw new InvalidOperationException($"Username '{user.Username}' da ton tai.");

        if (!string.IsNullOrEmpty(user.Phone) && await PhoneExistsAsync(user.Phone))
            throw new InvalidOperationException($"So dien thoai '{user.Phone}' da ton tai.");

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("User created: userCode={UserCode}, provider={Provider}",
            user.UserCode, user.SocialProvider ?? "password");
        return user;
    }

    public async Task<OperationResult> UpdateProfileAsync(Guid userUuid, UpdateProfileRequest model)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserUUID == userUuid);
        if (user is null)
            return OperationResult.Fail("NotFound", "Khong tim thay nguoi dung.");

        if (!string.IsNullOrWhiteSpace(model.Email))
        {
            if (!Validator.IsValidEmail(model.Email))
                return OperationResult.Fail("ValidationError", "Email khong dung dinh dang.");

            if (await EmailExistsAsync(model.Email, userUuid))
                return OperationResult.Fail("EmailExists", "Email da ton tai.");

            user.Email = model.Email;
        }

        if (!string.IsNullOrWhiteSpace(model.Username))
        {
            if (await UsernameExistsAsync(model.Username, userUuid))
                return OperationResult.Fail("UsernameExists", "Username da ton tai.");

            user.Username = model.Username;
        }

        if (!string.IsNullOrWhiteSpace(model.Phone))
        {
            if (!Validator.IsValidVietnamesePhone(model.Phone))
                return OperationResult.Fail("ValidationError", "So dien thoai khong dung dinh dang.");

            if (await PhoneExistsAsync(model.Phone, userUuid))
                return OperationResult.Fail("PhoneExists", "So dien thoai da ton tai.");

            user.Phone = model.Phone;
        }

        if (!string.IsNullOrWhiteSpace(model.Address)) user.Address = model.Address;
        if (!string.IsNullOrWhiteSpace(model.FirstName)) user.FirstName = model.FirstName;
        if (!string.IsNullOrWhiteSpace(model.LastName)) user.LastName = model.LastName;
        if (!string.IsNullOrWhiteSpace(model.IdentityNumber)) user.IdentityNumber = model.IdentityNumber;
        if (!string.IsNullOrWhiteSpace(model.Gender)) user.Gender = model.Gender;
        if (!string.IsNullOrWhiteSpace(model.Image)) user.Image = model.Image;
        if (model.DateOfBirth.HasValue) user.DateOfBirth = ToUtc(model.DateOfBirth.Value);

        user.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return OperationResult.Ok("Cap nhat thanh cong.", UserMapper.ToLoginResponse(user));
    }

    public async Task<bool> UpdatePasswordAsync(string username, string hash, string salt)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null)
            return false;

        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.UpdatedDate = DateTime.UtcNow;

        // Doi mat khau cung la co hoi mo khoa: nguoi dung quen mat khau roi bi khoa vi
        // nhap sai nhieu lan van phai vao duoc ngay sau khi dat lai.
        user.LoginAttempts = 0;
        user.IsLocked = false;
        user.LockUntil = null;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<UserDb?> RecordLoginAttemptAsync(
        Guid userUuid, int loginAttempts, bool isLocked, DateTime? lockUntil, DateTime? lastLogin)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserUUID == userUuid);
        if (user is null)
            return null;

        user.LoginAttempts = loginAttempts;
        user.IsLocked = isLocked;
        user.LockUntil = lockUntil.HasValue ? ToUtc(lockUntil.Value) : null;

        if (lastLogin.HasValue)
            user.LastLoginDate = ToUtc(lastLogin.Value);

        user.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return user;
    }

    // ─── Quan tri nguoi dung ──────────────────────────────────────────────────

    public async Task<UserListResponse> GetUsersAsync(UserListQueryRequest request)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var period = string.IsNullOrWhiteSpace(request.Period) ? "all" : request.Period.Trim().ToLowerInvariant();

        var query = _db.Users.AsNoTracking();

        var (fromDate, toDate) = ResolveDateRange(period, request.FromDate, request.ToDate);
        if (fromDate.HasValue)
            query = query.Where(u => u.CreatedDate >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(u => u.CreatedDate <= toDate.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = ToLikePattern(request.Search);
            query = query.Where(u =>
                (u.Email != null && EF.Functions.ILike(u.Email, pattern))
                || EF.Functions.ILike(u.Username, pattern)
                || (u.Phone != null && EF.Functions.ILike(u.Phone, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var pattern = ToLikePattern(request.Email);
            query = query.Where(u => u.Email != null && EF.Functions.ILike(u.Email, pattern));
        }

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var pattern = ToLikePattern(request.Username);
            query = query.Where(u => EF.Functions.ILike(u.Username, pattern));
        }

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var pattern = ToLikePattern(request.Phone);
            query = query.Where(u => u.Phone != null && EF.Functions.ILike(u.Phone, pattern));
        }

        if (request.IsActive.HasValue)
            query = query.Where(u => u.IsActive == request.IsActive.Value);

        if (request.IsLocked.HasValue)
            query = query.Where(u => u.IsLocked == request.IsLocked.Value);

        query = ApplySort(query, request.SortBy, request.SortDir);

        var totalCount = await query.CountAsync();
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new UserListResponse
        {
            Items = rows.Select(UserMapper.ToListItem).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            HasPreviousPage = page > 1,
            HasNextPage = totalPages > 0 && page < totalPages,
            Period = period,
            FromDate = fromDate,
            ToDate = toDate,
            Search = request.Search
        };
    }

    public async Task<OperationResult> SetLockStateAsync(Guid userUuid, UserLockRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserUUID == userUuid);
        if (user is null)
            return OperationResult.Fail("NotFound", "Khong tim thay nguoi dung.");

        user.IsLocked = request.IsLocked;
        user.LockUntil = request.IsLocked && request.LockUntil.HasValue ? ToUtc(request.LockUntil.Value) : null;

        // Mo khoa ma khong reset bo dem thi lan nhap sai ke tiep se khoa lai ngay.
        if (!request.IsLocked)
            user.LoginAttempts = 0;

        user.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return OperationResult.Ok(request.IsLocked ? "Da khoa tai khoan." : "Da mo khoa tai khoan.");
    }

    public async Task<OperationResult> SetActiveStateAsync(Guid userUuid, UserStatusRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserUUID == userUuid);
        if (user is null)
            return OperationResult.Fail("NotFound", "Khong tim thay nguoi dung.");

        user.IsActive = request.IsActive;
        user.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return OperationResult.Ok(request.IsActive ? "Da kich hoat tai khoan." : "Da vo hieu hoa tai khoan.");
    }

    // ─── Kiem tra trung ───────────────────────────────────────────────────────

    public Task<bool> EmailExistsAsync(string email, Guid? excludeUserUuid = null)
        => _db.Users.AnyAsync(u => u.Email == email && (!excludeUserUuid.HasValue || u.UserUUID != excludeUserUuid.Value));

    public Task<bool> PhoneExistsAsync(string phone, Guid? excludeUserUuid = null)
        => _db.Users.AnyAsync(u => u.Phone == phone && (!excludeUserUuid.HasValue || u.UserUUID != excludeUserUuid.Value));

    public Task<bool> UsernameExistsAsync(string username, Guid? excludeUserUuid = null)
        => _db.Users.AnyAsync(u => u.Username == username && (!excludeUserUuid.HasValue || u.UserUUID != excludeUserUuid.Value));

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static IQueryable<UserDb> ApplySort(IQueryable<UserDb> query, string? sortBy, string? sortDir)
    {
        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        return (sortBy ?? "createdDate").ToLowerInvariant() switch
        {
            "updateddate" => desc ? query.OrderByDescending(u => u.UpdatedDate) : query.OrderBy(u => u.UpdatedDate),
            "lastlogindate" => desc ? query.OrderByDescending(u => u.LastLoginDate) : query.OrderBy(u => u.LastLoginDate),
            "username" => desc ? query.OrderByDescending(u => u.Username) : query.OrderBy(u => u.Username),
            _ => desc ? query.OrderByDescending(u => u.CreatedDate) : query.OrderBy(u => u.CreatedDate)
        };
    }

    private static (DateTime? FromDate, DateTime? ToDate) ResolveDateRange(string period, DateTime? fromDate, DateTime? toDate)
    {
        var now = DateTime.UtcNow;
        var resolvedFrom = NormalizeUtc(fromDate);
        var resolvedTo = NormalizeUtc(toDate);

        if (period == "7d") resolvedFrom ??= now.AddDays(-7);
        if (period == "30d") resolvedFrom ??= now.AddDays(-30);

        // toDate chi co ngay (khong gio) nghia la "het ngay do", khong phai 00:00 —
        // neu khong, ban ghi tao luc 09:00 cung ngay se bi loai.
        if (resolvedTo.HasValue && resolvedTo.Value.TimeOfDay == TimeSpan.Zero)
            resolvedTo = resolvedTo.Value.Date.AddDays(1).AddTicks(-1);

        return (resolvedFrom, resolvedTo);
    }

    private static DateTime? NormalizeUtc(DateTime? value)
        => value.HasValue ? ToUtc(value.Value) : null;

    /// <summary>
    /// Npgsql yeu cau DateTime gui vao cot <c>timestamptz</c> phai co Kind = Utc, nem
    /// ngoai le voi Unspecified. Moi gia tri tu client deu di qua day truoc khi luu.
    /// </summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string ToLikePattern(string value)
    {
        var escaped = value.Trim()
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        return $"%{escaped}%";
    }
}
