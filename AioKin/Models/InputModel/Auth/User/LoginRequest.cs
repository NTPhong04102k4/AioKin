using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Auth.User;

public class LoginRequest
{
    [Required(ErrorMessage = "Username, phone hoac email la bat buoc.")]
    [MaxLength(100, ErrorMessage = "Username/phone/email toi da 100 ky tu.")]
    public string UsernameOrPhoneOrEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mat khau la bat buoc.")]
    [MinLength(6, ErrorMessage = "Mat khau toi thieu 6 ky tu.")]
    [MaxLength(128, ErrorMessage = "Mat khau toi da 128 ky tu.")]
    public string Password { get; set; } = string.Empty;
}

public class RefreshTokenRequest
{
    [Required(ErrorMessage = "Refresh token la bat buoc.")]
    public string RefreshToken { get; set; } = string.Empty;
}

public class LogoutRequest
{
    /// <summary>Refresh token de revoke khi logout. Bo trong thi chi blacklist access token.</summary>
    public string? RefreshToken { get; set; }
}
