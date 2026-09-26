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

    /// <summary>Id thiet bi client tu sinh va giu on dinh (vd UUID trong Keystore/Keychain). Bo trong van xac thuc duoc.</summary>
    [MaxLength(100, ErrorMessage = "DeviceId toi da 100 ky tu.")]
    public string? DeviceId { get; set; }

    [MaxLength(120, ErrorMessage = "DeviceName toi da 120 ky tu.")]
    public string? DeviceName { get; set; }

    /// <summary>"android" | "ios" | "web" — chi de hien thi trong /account/sessions.</summary>
    [MaxLength(20, ErrorMessage = "Platform toi da 20 ky tu.")]
    public string? Platform { get; set; }
}

public class ResendOtpRequest
{
    [Required(ErrorMessage = "Email la bat buoc")]
    [EmailAddress(ErrorMessage = "Email khong hop le")]
    public string Email { get; set; } = string.Empty;
}
