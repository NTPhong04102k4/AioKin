using StaffDb = AioKin.Data.Entities.Security.Staff;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Services.Auth.Token;

/// <summary>
/// Ky access token. Tach khoi <c>IUserService</c> de viec sinh token khong keo theo
/// DbContext: nguoi goi da co san entity, khong can tra database mot lan nua chi de
/// dien claim.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Token cho khach hang — role luon la Customer.</summary>
    string CreateForCustomer(UserDb user);

    /// <summary>Token cho tai khoan quan tri; <paramref name="roleName"/> lay tu Staff.Role.</summary>
    string CreateForStaff(StaffDb staff, string roleName);

    /// <summary>So giay song cua access token — dung cho truong <c>expires_in</c>.</summary>
    int AccessTokenLifetimeSeconds { get; }
}
