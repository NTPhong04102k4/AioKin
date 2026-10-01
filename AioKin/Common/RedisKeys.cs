namespace AioKin.Common;

/// <summary>
/// Tap trung tat ca Redis key pattern — tranh hardcode string rai rac trong code.
/// Quy uoc prefix: {domain}:{identifier}
/// </summary>
public static class RedisKeys
{
    // ─── Auth ─────────────────────────────────────────────────────────────────

    /// <summary>Refresh token (sau khi bam sha256) → RefreshTokenPayload JSON.</summary>
    public static string RefreshToken(string tokenHash) => $"auth:refresh:{tokenHash}";

    /// <summary>Tap hop refresh token cua mot user — dung de revoke tat ca.</summary>
    public static string UserRefreshTokens(string userCode) => $"auth:user_tokens:{userCode}";

    /// <summary>Access token session (sau khi bam sha256) → AccessTokenSession JSON.</summary>
    public static string AccessSession(string tokenHash) => $"auth:session:{tokenHash}";

    /// <summary>Tap hop hash cua access token dang song cua mot subject — dung de revoke tat ca.</summary>
    public static string UserAccessSessions(string subject) => $"auth:user_sessions:{subject}";

    /// <summary>Challenge dang cho verify, dung 1 lan.</summary>
    public static string BiometricChallenge(string challengeId) => $"auth:biometric_challenge:{challengeId}";

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
    /// Tran tren tuyet doi mac dinh cua ca chuoi refresh token (TokenFamilyId), tinh tu luc
    /// dang nhap dau tien — doc tu Jwt:RefreshTokenAbsoluteExpiryDays, fallback gia tri nay.
    /// </summary>
    public static readonly TimeSpan RefreshTokenAbsolute = TimeSpan.FromDays(60);

    /// <summary>
    /// TTL cua mot refresh token sau khi bi tombstone (da rotate, giu lai de bay phat hien
    /// replay). Ngan hon nhieu so RefreshToken TTL thuong: ke tan cong thuong chi replay token
    /// cu trong vai gio sau khi thiet bi that da xoay vong, giu lau hon chi ton bo nho Redis.
    /// </summary>
    public static readonly TimeSpan RefreshTokenTombstone = TimeSpan.FromHours(48);

    /// <summary>2 phut: du de nguoi dung xac thuc sinh trac, ngan de giam cua so tan cong.</summary>
    public static readonly TimeSpan BiometricChallenge = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Ngan co chu dich. Cache nay dung de tiet kiem mot lan JOIN, khong phai de giu lau —
    /// va no la cache cua mot quyet dinh phan quyen.
    /// </summary>
    public static readonly TimeSpan FamilyMembership = TimeSpan.FromMinutes(5);
}
