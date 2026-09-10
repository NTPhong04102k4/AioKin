using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Auth.Admin;

public class LoginAdminRequest
{
    [Required(ErrorMessage = "Username la bat buoc.")]
    [MaxLength(50, ErrorMessage = "Username toi da 50 ky tu.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mat khau la bat buoc.")]
    [MinLength(6, ErrorMessage = "Mat khau toi thieu 6 ky tu.")]
    [MaxLength(128, ErrorMessage = "Mat khau toi da 128 ky tu.")]
    public string Password { get; set; } = string.Empty;
}
