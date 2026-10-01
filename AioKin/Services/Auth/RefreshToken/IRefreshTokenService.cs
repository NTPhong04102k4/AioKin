using AioKin.Services.Auth.Token;

namespace AioKin.Services.Auth.RefreshToken;

/// <summary>
/// Refresh token opaque, luu trong Redis duoi khoa sha256(token). Trang thai nam o server
/// nen thu hoi co hieu luc ngay lap tuc.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>
    /// Tao token moi va luu vao Redis. <paramref name="device"/> co the DeviceInfo.Unknown
    /// (client cu chua gui hoac duong dang nhap khong doc duoc thiet bi) — cung dang tham so
    /// voi IAccessTokenService.CreateFor*Async de hai ben nhat quan.
    ///
    /// <paramref name="tokenFamilyId"/>/<paramref name="absoluteExpiresAt"/> de trong (null)
    /// cho MOI lan dang nhap moi — service tu sinh TokenFamilyId moi va tinh AbsoluteExpiresAt
    /// moi (now + Jwt:RefreshTokenAbsoluteExpiryDays). Khi XOAY VONG (rotation), goi voi gia
    /// tri lay tu payload cua token cu de giu nguyen ca chuoi va tran tuyet doi khong doi.
    /// </summary>
    Task<string> GenerateAsync(string subject, string role, DeviceInfo device, string? tokenFamilyId = null, DateTime? absoluteExpiresAt = null);

    /// <summary>
    /// Xac thuc. Null neu het han hoac khong ton tai. CHU Y: khong null khong co nghia la con
    /// dung duoc — co the la mot token da bi TombstoneAsync (IsRevoked == true), caller phai tu
    /// kiem tra co de phat hien replay (tai su dung mot token da bi xoay vong).
    /// </summary>
    Task<RefreshTokenPayload?> ValidateAsync(string token);

    /// <summary>Thu hoi han (xoa khoi Redis) mot token cu the — dung khi logout, khong dung cho rotation.</summary>
    Task RevokeAsync(string token);

    /// <summary>
    /// Danh dau mot token la da xoay vong (IsRevoked = true) nhung GIU LAI trong Redis voi TTL
    /// ngan (RedisTtl.RefreshTokenTombstone) thay vi xoa han — dung trong /auth/refresh-token
    /// thay cho RevokeAsync, de lan refresh ke tiep voi CUNG token nay (replay) con phat hien
    /// duoc thay vi chi thay "khong ton tai" nhu mot token het han binh thuong.
    /// </summary>
    Task TombstoneAsync(string token);

    /// <summary>
    /// Thu hoi han TOAN BO token cung TokenFamilyId cua mot subject — dung khi phat hien replay
    /// (ValidateAsync tra ve payload co IsRevoked == true): ca thiet bi that lan ke tan cong
    /// cam token cu deu bi ngat, buoc dang nhap lai.
    /// </summary>
    Task RevokeFamilyAsync(string subject, string tokenFamilyId);

    /// <summary>Thu hoi tat ca token cua mot subject — dung khi doi mat khau hoac nghi ngo lo tai khoan.</summary>
    Task RevokeAllAsync(string subject);

    /// <summary>Thu hoi tat ca token cua mot subject PHAT TU mot thiet bi cu the — dung khi "dang xuat thiet bi nay".</summary>
    Task RevokeAllForDeviceAsync(string subject, string? deviceId);
}
