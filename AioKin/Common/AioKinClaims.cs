using System.Security.Claims;

namespace AioKin.Common;

/// <summary>
/// Claim rieng cua he thong. Dung ten tuy chinh thay vi cac claim JWT chuan vi
/// <c>JwtSecurityTokenHandler</c> anh xa <c>name</c> va <c>unique_name</c> ve cung
/// <see cref="ClaimTypes.Name"/> — doc <c>ClaimTypes.Name</c> se ra ho ten hay username
/// tuy thu tu claim, mot nham lan im lang. Cac ten duoi day khong nam trong bang anh xa
/// mac dinh nen luon doc ra dung thu da ghi vao.
/// </summary>
public static class AioKinClaims
{
    /// <summary>Username dang nhap — dinh danh on dinh cho ca User lan Staff.</summary>
    public const string Username = "username";

    /// <summary>UserCode cua khach hang.</summary>
    public const string UserCode = "user_code";

    /// <summary>StaffID cua tai khoan quan tri.</summary>
    public const string StaffId = "staff_id";

    /// <summary>Access token goc (khong phai jti) — cho phep Logout revoke dung session nay
    /// ma khong phai parse lai header Authorization.</summary>
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

    /// <summary>UserUUID cua khach hang, doc tu claim <c>sub</c>. Null neu token la cua Staff.</summary>
    public static Guid? GetUserUuid(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static int? GetStaffId(this ClaimsPrincipal principal)
        => int.TryParse(principal.FindFirstValue(AioKinClaims.StaffId), out var id) ? id : null;

    public static string? GetJti(this ClaimsPrincipal principal)
        => principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti);

    public static string? GetSessionToken(this ClaimsPrincipal principal)
        => principal.FindFirstValue(AioKinClaims.SessionToken);
}
