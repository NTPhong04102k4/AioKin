namespace AioKin.Services.Auth.Otp;

public interface IOtpService
{
    /// <summary>Sinh OTP 6 chu so va luu vao Redis. Nem <see cref="InvalidOperationException"/> neu khong luu duoc.</summary>
    Task<string> GenerateOtpAsync(string email);

    /// <summary>Xac thuc OTP. Dung thi xoa key luon nen moi ma chi dung duoc mot lan.</summary>
    Task<bool> VerifyOtpAsync(string email, string otpCode);

    Task<bool> IsOtpExpiredAsync(string email);
}

/// <summary>Ban ghi OTP trong Redis. Attempts chan viec do 1.000.000 kha nang.</summary>
public class OtpData
{
    public string Code { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
}
