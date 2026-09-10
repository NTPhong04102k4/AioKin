using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Auth.User;

public class VerifyOtpRequest
{
    [Required(ErrorMessage = "Email la bat buoc")]
    [EmailAddress(ErrorMessage = "Email khong hop le")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ma OTP la bat buoc")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "Ma OTP phai co dung 6 ky tu")]
    public string OtpCode { get; set; } = string.Empty;
}

public class ResendOtpRequest
{
    [Required(ErrorMessage = "Email la bat buoc")]
    [EmailAddress(ErrorMessage = "Email khong hop le")]
    public string Email { get; set; } = string.Empty;
}
