namespace AioKin.Services.Auth.Email;

/// <summary>
/// Ban du phong cho moi truong Development khi chua co API key Brevo: ghi noi dung ra log
/// thay vi gui that. Nho vay luong dang ky va quen mat khau van chay duoc tu dau den cuoi
/// tren may dev — lay ma OTP ngay trong cua so console.
///
/// Chi duoc dang ky khi <see cref="BrevoOptions.IsConfigured"/> la false VA moi truong
/// khong phai Production (xem Program.cs).
/// </summary>
public class LoggingEmailService : IEmailService
{
    private readonly ILogger<LoggingEmailService> _logger;

    public LoggingEmailService(ILogger<LoggingEmailService> logger)
    {
        _logger = logger;
        _logger.LogWarning(
            "IEmailService dang chay o che do LOG-ONLY — khong co email nao duoc gui that. "
            + "Dat Email:ApiKey va Email:SenderEmail de bat Brevo.");
    }

    public Task<bool> SendOtpEmailAsync(string email, string otpCode, string username)
        => Log("OTP dang ky", email, $"username={username} otp={otpCode}");

    public Task<bool> SendWelcomeEmailAsync(string email, string username)
        => Log("Chao mung", email, $"username={username}");

    public Task<bool> SendPasswordResetOtpEmailAsync(string email, string otpCode, string username)
        => Log("OTP quen mat khau", email, $"username={username} otp={otpCode}");

    public Task<bool> SendTemporaryPasswordEmailAsync(string email, string temporaryPassword, string username)
        => Log("Mat khau tam", email, $"username={username} tempPassword={temporaryPassword}");

    public Task<bool> SendPasswordChangedNoticeAsync(string email, string username)
        => Log("Thong bao doi mat khau", email, $"username={username}");

    public Task<bool> SendContactEmailAsync(string fromUserEmail, string subject, string message)
        => Log("Lien he", fromUserEmail, $"subject={subject} message={message}");

    private Task<bool> Log(string kind, string recipient, string details)
    {
        _logger.LogInformation("[EMAIL:{Kind}] to={Recipient} {Details}", kind, recipient, details);
        return Task.FromResult(true);
    }
}
