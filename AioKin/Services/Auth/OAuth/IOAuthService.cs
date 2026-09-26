using AioKin.Models.ViewModel.Auth.User;
using AioKin.Services.Auth.Token;
using Microsoft.AspNetCore.Authentication;

namespace AioKin.Services.Auth.OAuth;

/// <summary>
/// Hoan tat dang nhap SSO. Middleware OAuth cua ASP.NET Core da lo phan doi code lay
/// token va dat cookie tam; cac phuong thuc o day nhan ket qua do, doc ho so tu nha
/// cung cap, tao hoac cap nhat user, roi cap token cua he thong.
/// </summary>
public interface IOAuthService
{
    Task<OAuthResult> CompleteGoogleLoginAsync(AuthenticateResult externalAuth);

    Task<OAuthResult> CompleteFacebookLoginAsync(AuthenticateResult externalAuth);

    /// <summary>Dang nhap Google tu id_token do SDK native (vd React Native Google Sign-In) tra ve truc tiep.</summary>
    Task<OAuthResult> CompleteGoogleTokenLoginAsync(string idToken, DeviceInfo device);

    /// <summary>Dang nhap Facebook tu access token do SDK native (vd React Native FBSDK) tra ve truc tiep.</summary>
    Task<OAuthResult> CompleteFacebookTokenLoginAsync(string accessToken, DeviceInfo device);
}

public class OAuthResult
{
    public bool Success { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }
    public LoginResponse? UserData { get; init; }

    public static OAuthResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
