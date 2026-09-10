namespace AioKin.Services.Auth.PasswordUser;

/// <summary>
/// Mat khau tam 8 ky tu song 3 phut trong Redis. Buoc trung gian cua luong quen mat khau:
/// nguoi dung xac thuc OTP → nhan mat khau tam → doi mat khau moi.
/// </summary>
public interface ITemporaryPasswordService
{
    Task<string> GenerateTemporaryPasswordAsync(string email);

    /// <summary>Xac thuc. Dung thi xoa key luon nen mat khau tam chi dung duoc mot lan.</summary>
    Task<bool> VerifyTemporaryPasswordAsync(string email, string tempPassword);

    Task<bool> IsTemporaryPasswordExpiredAsync(string email);
}

public class TemporaryPasswordData
{
    public string Password { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
