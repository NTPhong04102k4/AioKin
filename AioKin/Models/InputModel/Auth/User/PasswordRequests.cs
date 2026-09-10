using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Auth.User;

public class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

/// <summary>Dat lai mat khau bang mat khau tam nhan qua email (8 ky tu, TTL 3 phut).</summary>
public class ResetPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(8, MinimumLength = 8)]
    public string TemporaryPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 6)]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Buoc 2 cua luong quen mat khau qua OTP: xac thuc OTP de nhan mat khau tam.</summary>
public class VerifyOtpForPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string OtpCode { get; set; } = string.Empty;
}

/// <summary>Doi mat khau khi da dang nhap — can mat khau hien tai.</summary>
public class ChangePasswordRequest
{
    [Required(ErrorMessage = "Mat khau hien tai la bat buoc")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mat khau moi la bat buoc")]
    [StringLength(128, MinimumLength = 6, ErrorMessage = "Mat khau moi phai co it nhat 6 ky tu")]
    public string NewPassword { get; set; } = string.Empty;
}
