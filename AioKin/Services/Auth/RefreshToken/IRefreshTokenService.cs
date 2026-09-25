namespace AioKin.Services.Auth.RefreshToken;

/// <summary>
/// Refresh token opaque, luu trong Redis duoi khoa sha256(token). Trang thai nam o server
/// nen thu hoi co hieu luc ngay lap tuc.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Tao token moi va luu vao Redis. <paramref name="deviceId"/> co the null (client cu chua gui).</summary>
    Task<string> GenerateAsync(string subject, string role, string? deviceId);

    /// <summary>Xac thuc. Null neu het han hoac khong ton tai.</summary>
    Task<RefreshTokenPayload?> ValidateAsync(string token);

    /// <summary>Thu hoi mot token cu the — dung khi logout hoac khi xoay vong token.</summary>
    Task RevokeAsync(string token);

    /// <summary>Thu hoi tat ca token cua mot subject — dung khi doi mat khau hoac nghi ngo lo tai khoan.</summary>
    Task RevokeAllAsync(string subject);

    /// <summary>Thu hoi tat ca token cua mot subject PHAT TU mot thiet bi cu the — dung khi "dang xuat thiet bi nay".</summary>
    Task RevokeAllForDeviceAsync(string subject, string? deviceId);
}
