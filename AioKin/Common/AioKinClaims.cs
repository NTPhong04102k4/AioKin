using System.Security.Claims;

namespace AioKin.Common;

/// <summary>
/// Claim rieng cua he thong, do OpaqueAccessTokenAuthenticationHandler gan thu cong vao
/// ClaimsIdentity (khong con JwtSecurityTokenHandler anh xa claim tu dong nhu truoc).
/// Dung ten tuy chinh thay vi doc thang username/ho ten qua <see cref="ClaimTypes.Name"/>
/// de tranh nham lan giua hai gia tri do — cac ten duoi day luon doc ra dung thu da ghi vao.
/// </summary>
public static class AioKinClaims
{
    /// <summary>Username dang nhap — dinh danh on dinh cho ca User lan Staff.</summary>
    public const string Username = "username";

    /// <summary>UserCode cua khach hang.</summary>
    public const string UserCode = "user_code";

    /// <summary>StaffID cua tai khoan quan tri.</summary>
    public const string StaffId = "staff_id";

    /// <summary>Hash (sha256) cua access token dang dung — cho phep Logout/GetSessions doi
    /// chieu dung session nay ma khong phai parse lai header Authorization. KHONG con la
    /// token goc: tu plan session-management, claim nay khong bao gio giu raw token nua,
    /// nen an toan hon de log/luu tam so voi truoc.</summary>
    public const string SessionToken = "session_token";
}

public static class ClaimsPrincipalExtensions
{
    public static string? GetUsername(this ClaimsPrincipal principal)
        => principal.FindFirstValue(AioKinClaims.Username);

    public static string? GetUserCode(this ClaimsPrincipal principal)
        => principal.FindFirstValue(AioKinClaims.UserCode);

    public static string? GetRole(this ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.Role);

    /// <summary>UserUUID cua khach hang, doc tu <see cref="ClaimTypes.NameIdentifier"/> (do
    /// OpaqueAccessTokenAuthenticationHandler gan). Null neu token la cua Staff.</summary>
    public static Guid? GetUserUuid(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static int? GetStaffId(this ClaimsPrincipal principal)
        => int.TryParse(principal.FindFirstValue(AioKinClaims.StaffId), out var id) ? id : null;

    public static string? GetSessionToken(this ClaimsPrincipal principal)
        => principal.FindFirstValue(AioKinClaims.SessionToken);
}
