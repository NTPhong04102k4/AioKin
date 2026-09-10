using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AioKin.Common;
using Microsoft.Extensions.Options;

namespace AioKin.Services.Auth.Email;

/// <summary>
/// Gui email qua REST API transactional cua Brevo (<c>POST /v3/smtp/email</c>).
/// Dung HTTP truc tiep thay vi SDK: chi can mot endpoint, khong dang doi lay them mot
/// package va vong doi cap nhat cua no.
/// </summary>
public class BrevoEmailService : IEmailService
{
    private readonly HttpClient _http;
    private readonly BrevoOptions _options;
    private readonly ILogger<BrevoEmailService> _logger;

    public BrevoEmailService(HttpClient http, IOptions<BrevoOptions> options, ILogger<BrevoEmailService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public Task<bool> SendOtpEmailAsync(string email, string otpCode, string username)
    {
        var (subject, html) = EmailTemplates.Otp(username, otpCode, (int)RedisTtl.Otp.TotalMinutes);
        return SendAsync(email, username, subject, html);
    }

    public Task<bool> SendWelcomeEmailAsync(string email, string username)
    {
        var (subject, html) = EmailTemplates.Welcome(username);
        return SendAsync(email, username, subject, html);
    }

    public Task<bool> SendPasswordResetOtpEmailAsync(string email, string otpCode, string username)
    {
        var (subject, html) = EmailTemplates.PasswordResetOtp(username, otpCode, (int)RedisTtl.Otp.TotalMinutes);
        return SendAsync(email, username, subject, html);
    }

    public Task<bool> SendTemporaryPasswordEmailAsync(string email, string temporaryPassword, string username)
    {
        var (subject, html) = EmailTemplates.TemporaryPassword(username, temporaryPassword, (int)RedisTtl.TempPassword.TotalMinutes);
        return SendAsync(email, username, subject, html);
    }

    public Task<bool> SendPasswordChangedNoticeAsync(string email, string username)
    {
        var (subject, html) = EmailTemplates.PasswordChanged(username);
        return SendAsync(email, username, subject, html);
    }

    public Task<bool> SendContactEmailAsync(string fromUserEmail, string subject, string message)
    {
        var (mailSubject, html) = EmailTemplates.Contact(fromUserEmail, subject, message);
        var support = string.IsNullOrWhiteSpace(_options.SupportEmail) ? _options.SenderEmail : _options.SupportEmail;

        // replyTo = nguoi dung, de nhan vien ho tro bam Reply la tra loi dung nguoi.
        return SendAsync(support, "Support", mailSubject, html, replyTo: fromUserEmail);
    }

    private async Task<bool> SendAsync(string toEmail, string toName, string subject, string htmlContent, string? replyTo = null)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogError("Brevo chua duoc cau hinh (Email:ApiKey / Email:SenderEmail) — khong gui duoc toi {To}.", toEmail);
            return false;
        }

        var payload = new BrevoSendRequest
        {
            Sender = new BrevoContact { Email = _options.SenderEmail, Name = _options.SenderName },
            To = [new BrevoContact { Email = toEmail, Name = toName }],
            Subject = subject,
            HtmlContent = htmlContent,
            ReplyTo = replyTo is null ? null : new BrevoContact { Email = replyTo }
        };

        try
        {
            using var response = await _http.PostAsJsonAsync("smtp/email", payload);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Brevo accepted email to={To} subject={Subject}", toEmail, subject);
                return true;
            }

            // Doc body loi: Brevo mo ta ro nguyen nhan (sender chua verify, het quota...)
            // ma status code khong noi len duoc.
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Brevo rejected email to={To}, status={Status}, body={Body}",
                toEmail, (int)response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Brevo request failed for to={To}", toEmail);
            return false;
        }
    }

    // ─── Khuon payload cua Brevo ──────────────────────────────────────────────

    private sealed class BrevoSendRequest
    {
        [JsonPropertyName("sender")]
        public BrevoContact Sender { get; init; } = new();

        [JsonPropertyName("to")]
        public List<BrevoContact> To { get; init; } = [];

        [JsonPropertyName("subject")]
        public string Subject { get; init; } = string.Empty;

        [JsonPropertyName("htmlContent")]
        public string HtmlContent { get; init; } = string.Empty;

        [JsonPropertyName("replyTo")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public BrevoContact? ReplyTo { get; init; }
    }

    private sealed class BrevoContact
    {
        [JsonPropertyName("email")]
        public string Email { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; init; }
    }
}
