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

    /// <summary>Id thiet bi client tu sinh va giu on dinh (vd UUID trong Keystore/Keychain). Bo trong van dang nhap duoc.</summary>
    [MaxLength(100, ErrorMessage = "DeviceId toi da 100 ky tu.")]
    public string? DeviceId { get; set; }

    [MaxLength(120, ErrorMessage = "DeviceName toi da 120 ky tu.")]
    public string? DeviceName { get; set; }

    /// <summary>"android" | "ios" | "web" — chi de hien thi trong /account/sessions.</summary>
    [MaxLength(20, ErrorMessage = "Platform toi da 20 ky tu.")]
    public string? Platform { get; set; }
}

public class RefreshTokenRequest
{
    [Required(ErrorMessage = "Refresh token la bat buoc.")]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Id thiet bi client tu sinh va giu on dinh (vd UUID trong Keystore/Keychain). Bo trong van refresh duoc.</summary>
    [MaxLength(100, ErrorMessage = "DeviceId toi da 100 ky tu.")]
    public string? DeviceId { get; set; }

    [MaxLength(120, ErrorMessage = "DeviceName toi da 120 ky tu.")]
    public string? DeviceName { get; set; }

    /// <summary>"android" | "ios" | "web" — chi de hien thi trong /account/sessions.</summary>
    [MaxLength(20, ErrorMessage = "Platform toi da 20 ky tu.")]
    public string? Platform { get; set; }
}

public class LogoutRequest
{
    /// <summary>Refresh token de revoke khi logout. Bo trong thi chi blacklist access token.</summary>
    public string? RefreshToken { get; set; }
}

/// <summary>Dang nhap Google bang SDK native (RN Google Sign-In), khac voi luong popup /auth/login/google.</summary>
public class GoogleNativeLoginRequest
{
    /// <summary>ID token JWT tra ve tu GoogleSignin.signIn() phia client. Server tu verify, khong tin client.</summary>
    [Required(ErrorMessage = "idToken la bat buoc.")]
    public string IdToken { get; set; } = string.Empty;

    [MaxLength(100, ErrorMessage = "DeviceId toi da 100 ky tu.")]
    public string? DeviceId { get; set; }

    [MaxLength(120, ErrorMessage = "DeviceName toi da 120 ky tu.")]
    public string? DeviceName { get; set; }

    [MaxLength(20, ErrorMessage = "Platform toi da 20 ky tu.")]
    public string? Platform { get; set; }
}

/// <summary>Dang nhap Facebook bang SDK native (RN FBSDK), khac voi luong popup /auth/login/facebook.</summary>
public class FacebookNativeLoginRequest
{
    /// <summary>Access token tra ve tu LoginManager.logInWithPermissions() phia client. Server tu verify qua debug_token.</summary>
    [Required(ErrorMessage = "accessToken la bat buoc.")]
    public string AccessToken { get; set; } = string.Empty;

    [MaxLength(100, ErrorMessage = "DeviceId toi da 100 ky tu.")]
    public string? DeviceId { get; set; }

    [MaxLength(120, ErrorMessage = "DeviceName toi da 120 ky tu.")]
    public string? DeviceName { get; set; }

    [MaxLength(20, ErrorMessage = "Platform toi da 20 ky tu.")]
    public string? Platform { get; set; }
}
