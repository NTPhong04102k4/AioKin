using System.ComponentModel.DataAnnotations;

namespace AioKin.Services.Auth.Email;

/// <summary>
/// Gui email giao dich cho cac luong auth. Moi phuong thuc tra <c>bool</c> thay vi nem
/// ngoai le: nguoi goi luon phai quyet dinh xem gui that bai co lam hong nghiep vu hay
/// khong (dang ky thi co, email chao mung thi khong).
/// </summary>
public interface IEmailService
{
    /// <summary>OTP xac thuc email khi dang ky.</summary>
    Task<bool> SendOtpEmailAsync(string email, string otpCode, string username);

    /// <summary>Email chao mung sau khi dang ky thanh cong.</summary>
    Task<bool> SendWelcomeEmailAsync(string email, string username);

    /// <summary>OTP cho luong quen mat khau.</summary>
    Task<bool> SendPasswordResetOtpEmailAsync(string email, string otpCode, string username);

    /// <summary>Mat khau tam sau khi nguoi dung da xac thuc OTP.</summary>
    Task<bool> SendTemporaryPasswordEmailAsync(string email, string temporaryPassword, string username);

    /// <summary>Thong bao mat khau vua duoc doi — de nguoi dung phat hien neu khong phai ho lam.</summary>
    Task<bool> SendPasswordChangedNoticeAsync(string email, string username);

    /// <summary>Nguoi dung gui lien he ve hom thu ho tro.</summary>
    Task<bool> SendContactEmailAsync(string fromUserEmail, string subject, string message);
}

/// <summary>Cau hinh Brevo (truoc day la Sendinblue), doc tu section <c>Email</c>.</summary>
public class BrevoOptions
{
    public const string SectionName = "Email";

    /// <summary>API key transactional cua Brevo (<c>xkeysib-...</c>). Bo trong = chay che do log.</summary>
    public string? ApiKey { get; set; }

    [Required]
    [EmailAddress]
    public string SenderEmail { get; set; } = string.Empty;

    [Required]
    public string SenderName { get; set; } = "AioKin";

    /// <summary>Hom thu nhan form lien he. Bo trong thi dung <see cref="SenderEmail"/>.</summary>
    [EmailAddress]
    public string? SupportEmail { get; set; }

    /// <summary>Endpoint transactional cua Brevo. Chi doi khi test voi server gia.</summary>
    public string BaseUrl { get; set; } = "https://api.brevo.com/v3/";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SenderEmail);
}
