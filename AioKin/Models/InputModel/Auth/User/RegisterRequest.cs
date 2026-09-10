using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Auth.User;

public class RegisterRequest
{
    [Required(ErrorMessage = "Username la bat buoc")]
    [StringLength(50, ErrorMessage = "Username khong duoc vuot qua 50 ky tu")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email la bat buoc")]
    [EmailAddress(ErrorMessage = "Email khong hop le")]
    [StringLength(100, ErrorMessage = "Email khong duoc vuot qua 100 ky tu")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mat khau la bat buoc")]
    [StringLength(128, MinimumLength = 6, ErrorMessage = "Mat khau phai co it nhat 6 ky tu")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Ban ghi dang ky nam trong Redis giua buoc /register va buoc verify OTP.
/// Chi giu hash + salt, khong bao gio giu mat khau thuong.
/// </summary>
public class PendingRegistration
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
}
