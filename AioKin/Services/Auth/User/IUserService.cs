using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Auth.User;
using StaffDb = AioKin.Data.Entities.Security.Staff;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Services.Auth.User;

public interface IUserService
{
    // ─── Tra cuu ──────────────────────────────────────────────────────────────

    /// <summary>Tim theo username, email hoac so dien thoai — ba cach dang nhap deu vao day.</summary>
    Task<UserDb?> GetByUsernameOrEmailAsync(string identifier);

    Task<UserDb?> GetByUserCodeAsync(string userCode);

    Task<UserDb?> GetByUuidAsync(Guid userUuid);

    /// <summary>Tim theo subject id cua Google/Facebook.</summary>
    Task<UserDb?> GetBySocialIdAsync(string socialId);

    /// <summary>Tra Staff theo Username — dung cho luong refresh token cua phia quan tri.</summary>
    Task<StaffDb?> GetStaffByUsernameAsync(string username);

    // ─── Ghi ──────────────────────────────────────────────────────────────────

    /// <summary>Them user moi. Nem <see cref="InvalidOperationException"/> khi trung email/username/phone.</summary>
    Task<UserDb> CreateUserAsync(UserDb user);

    Task<OperationResult> UpdateProfileAsync(Guid userUuid, UpdateProfileRequest model);

    /// <summary>Doi mat khau theo username. Tra false neu khong tim thay user.</summary>
    Task<bool> UpdatePasswordAsync(string username, string hash, string salt);

    /// <summary>Ghi lai ket qua mot lan dang nhap: so lan sai, trang thai khoa, thoi diem dang nhap.</summary>
    Task<UserDb?> RecordLoginAttemptAsync(Guid userUuid, int loginAttempts, bool isLocked, DateTime? lockUntil, DateTime? lastLogin);

    // ─── Quan tri nguoi dung ──────────────────────────────────────────────────

    Task<UserListResponse> GetUsersAsync(UserListQueryRequest query);

    Task<OperationResult> SetLockStateAsync(Guid userUuid, UserLockRequest request);

    Task<OperationResult> SetActiveStateAsync(Guid userUuid, UserStatusRequest request);

    // ─── Kiem tra trung ───────────────────────────────────────────────────────

    Task<bool> UsernameExistsAsync(string username, Guid? excludeUserUuid = null);

    Task<bool> PhoneExistsAsync(string phone, Guid? excludeUserUuid = null);

    Task<bool> EmailExistsAsync(string email, Guid? excludeUserUuid = null);
}
