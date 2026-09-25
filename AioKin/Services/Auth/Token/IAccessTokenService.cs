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
    Task<string> CreateForCustomerAsync(UserDb user);

    /// <summary>Token cho tai khoan quan tri; <paramref name="roleName"/> lay tu Staff.Role.</summary>
    Task<string> CreateForStaffAsync(StaffDb staff, string roleName);

    /// <summary>Tra ve session neu token con hop le, null neu khong ton tai hoac het han.</summary>
    Task<AccessTokenSession?> ValidateAsync(string token);

    /// <summary>Thu hoi dung mot access token — dung khi logout.</summary>
    Task RevokeAsync(string token);

    /// <summary>Thu hoi moi access token dang song cua mot subject (UserCode hoac Username).</summary>
    Task RevokeAllForSubjectAsync(string subject);

    /// <summary>So giay song cua access token — dung cho truong <c>expires_in</c>.</summary>
    int AccessTokenLifetimeSeconds { get; }
}
