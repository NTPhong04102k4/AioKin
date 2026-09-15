namespace AioKin.Common;

/// <summary>
/// Tap trung tat ca Redis key pattern — tranh hardcode string rai rac trong code.
/// Quy uoc prefix: {domain}:{identifier}
/// </summary>
public static class RedisKeys
{
    // ─── Auth ─────────────────────────────────────────────────────────────────

    /// <summary>Blacklist JWT theo JTI. TTL = thoi gian con lai cua access token.</summary>
    public static string JwtBlacklist(string jti) => $"auth:blacklist:{jti}";

    /// <summary>Refresh token → {userCode}|{role}.</summary>
    public static string RefreshToken(string token) => $"auth:refresh:{token}";

    /// <summary>Tap hop refresh token cua mot user — dung de revoke tat ca.</summary>
    public static string UserRefreshTokens(string userCode) => $"auth:user_tokens:{userCode}";

    // ─── OTP / TempPwd / Registration ────────────────────────────────────────

    /// <summary>OTP data cho email.</summary>
    public static string Otp(string email) => $"auth:otp:{Normalize(email)}";

    /// <summary>Chan spam resend OTP.</summary>
    public static string OtpCooldown(string email) => $"auth:otp_cooldown:{Normalize(email)}";

    /// <summary>Mat khau tam thoi cho luong quen mat khau.</summary>
    public static string TempPassword(string email) => $"auth:temp_pwd:{Normalize(email)}";

    /// <summary>Du lieu dang ky dang cho verify OTP.</summary>
    public static string Registration(string email) => $"auth:registration:{Normalize(email)}";

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    // ─── Family ───────────────────────────────────────────────────────────────

    /// <summary>Tu cach thanh vien da phan giai. Xoa NGAY khi doi vai tro hoac go thanh vien.</summary>
    public static string FamilyMembership(Guid familyUuid, Guid userUuid)
        => $"family:{familyUuid}:member:{userUuid}";
}

/// <summary>TTL mac dinh cho tung loai key.</summary>
public static class RedisTtl
{
    public static readonly TimeSpan Otp = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan OtpCooldown = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan TempPassword = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan Registration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshToken = TimeSpan.FromDays(7);

    /// <summary>
    /// Ngan co chu dich. Cache nay dung de tiet kiem mot lan JOIN, khong phai de giu lau —
    /// va no la cache cua mot quyet dinh phan quyen.
    /// </summary>
    public static readonly TimeSpan FamilyMembership = TimeSpan.FromMinutes(5);
}
