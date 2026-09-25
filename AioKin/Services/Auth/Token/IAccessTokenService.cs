using StaffDb = AioKin.Data.Entities.Security.Staff;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Services.Auth.Token;

/// <summary>
/// Access token opaque, trang thai nam o server (Redis) thay vi tu chua nhu JWT. Thu hoi
/// la xoa key — co hieu luc ngay, khong can blacklist song song.
/// </summary>
public interface IAccessTokenService
{
    /// <summary>Token cho khach hang — role luon la Customer.</summary>
    Task<string> CreateForCustomerAsync(UserDb user, DeviceInfo device);

    /// <summary>Token cho tai khoan quan tri; <paramref name="roleName"/> lay tu Staff.Role.</summary>
    Task<string> CreateForStaffAsync(StaffDb staff, string roleName, DeviceInfo device);

    /// <summary>Tra ve session neu token con hop le, null neu khong ton tai hoac het han.</summary>
    Task<AccessTokenSession?> ValidateAsync(string token);

    /// <summary>Thu hoi dung mot access token — dung khi logout.</summary>
    Task RevokeAsync(string token);

    /// <summary>
    /// Thu hoi dung mot access session bang hash (khong phai raw token) — dung khi claim
    /// session_token trong HttpContext.User da la hash san (Logout), tranh phai giu raw
    /// token trong ClaimsPrincipal.
    /// </summary>
    Task RevokeByHashAsync(string hash);

    /// <summary>Thu hoi moi access token dang song cua mot subject (UserCode hoac Username).</summary>
    Task RevokeAllForSubjectAsync(string subject);

    /// <summary>Moi access session dang song cua mot subject, kem id cong khai (12 ky tu dau cua hash).
    /// Nhan tien don cac hash da chet (session het han/bi thu hoi noi khac) khoi danh sach theo doi.</summary>
    Task<IReadOnlyList<(string Id, AccessTokenSession Session)>> ListSessionsAsync(string subject);

    /// <summary>
    /// Thu hoi cac access session dang song cua mot subject PHAT TU mot thiet bi cu the —
    /// dung khi refresh (bo session cu cua cung thiet bi, tranh trung lap trong danh sach
    /// phien) va khi "dang xuat thiet bi nay".
    /// </summary>
    Task RevokeForDeviceAsync(string subject, string deviceId);

    /// <summary>So giay song cua access token — dung cho truong <c>expires_in</c>.</summary>
    int AccessTokenLifetimeSeconds { get; }
}
