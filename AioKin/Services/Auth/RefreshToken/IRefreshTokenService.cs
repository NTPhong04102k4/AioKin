namespace AioKin.Services.Auth.RefreshToken;

/// <summary>
/// Refresh token opaque (khong phai JWT) luu trong Redis. Vi trang thai nam o server nen
/// thu hoi co hieu luc ngay lap tuc — dieu ma mot JWT tu chua khong lam duoc.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Tao token moi va luu vao Redis. Tra ve chuoi token de gui cho client.</summary>
    Task<string> GenerateAsync(string userCode, string role);

    /// <summary>Xac thuc. Tra ve (userCode, role) neu hop le, null neu het han hoac khong ton tai.</summary>
    Task<(string UserCode, string Role)?> ValidateAsync(string token);

    /// <summary>Thu hoi mot token cu the — dung khi logout hoac khi xoay vong token.</summary>
    Task RevokeAsync(string token);

    /// <summary>Thu hoi tat ca token cua mot user — dung khi doi mat khau hoac nghi ngo lo tai khoan.</summary>
    Task RevokeAllAsync(string userCode);
}
