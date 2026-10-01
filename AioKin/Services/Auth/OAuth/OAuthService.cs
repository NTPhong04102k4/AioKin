using System.Security.Claims;
using System.Text.Json;
using AioKin.Common;
using AioKin.Models.Transfers.ProfileUser;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.Token;
using AioKin.Services.Auth.User;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authentication;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Services.Auth.OAuth;

public class OAuthService : IOAuthService
{
    private const string GoogleUserInfoUrl = "https://openidconnect.googleapis.com/v1/userinfo";
    private const string FacebookGraphUrl =
        "https://graph.facebook.com/v18.0/me?fields=id,name,email,picture,birthday,gender,location,hometown&access_token=";

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserService _userService;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ILogger<OAuthService> _logger;

    public OAuthService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IUserService userService,
        IAccessTokenService accessTokenService,
        IRefreshTokenService refreshTokenService,
        ILogger<OAuthService> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _userService = userService;
        _accessTokenService = accessTokenService;
        _refreshTokenService = refreshTokenService;
        _logger = logger;
    }

    // ─── Google ───────────────────────────────────────────────────────────────

    public async Task<OAuthResult> CompleteGoogleLoginAsync(AuthenticateResult externalAuth)
    {
        if (externalAuth?.Principal is null || !externalAuth.Succeeded)
            return OAuthResult.Fail("Xac thuc Google chua hoan tat.");

        try
        {
            var dto = await ReadGoogleProfileAsync(externalAuth);

            if (string.IsNullOrWhiteSpace(dto.IDSocial))
                return OAuthResult.Fail("Khong doc duoc thong tin tai khoan Google.");

            var (user, error) = await ResolveSocialUserAsync(
                dto.IDSocial,
                dto.Email,
                "google",
                dto.VerifiedEmail,
                () => UserMapper.FromGoogle(dto));

            return user is null ? OAuthResult.Fail(error!) : await IssueTokensAsync(user, DeviceInfo.Resolve(null, null, null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google OAuth failed");
            return OAuthResult.Fail("Da xay ra loi khi dang nhap Google.");
        }
    }

    /// <summary>Dang nhap Google tu id_token do SDK native (React Native Google Sign-In) gui len truc tiep.</summary>
    public async Task<OAuthResult> CompleteGoogleTokenLoginAsync(string idToken, DeviceInfo device)
    {
        try
        {
            var dto = await VerifyGoogleIdTokenAsync(idToken);
            if (dto is null)
                return OAuthResult.Fail("Google token khong hop le hoac da het han.");

            _logger.LogInformation("Google native: token ok, resolving user (hasEmail={HasEmail})", !string.IsNullOrWhiteSpace(dto.Email));
            var (user, error) = await ResolveSocialUserAsync(
                dto.IDSocial,
                dto.Email,
                "google",
                dto.VerifiedEmail,
                () => UserMapper.FromGoogle(dto));

            _logger.LogInformation("Google native: user resolved ok={Ok} error={Error}", user is not null, error);
            return user is null ? OAuthResult.Fail(error!) : await IssueTokensAsync(user, device);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google native token login failed");
            return OAuthResult.Fail("Da xay ra loi khi dang nhap Google.");
        }
    }

    /// <summary>
    /// Xac thuc chu ky va audience cua id_token voi Google truc tiep — khong di qua middleware
    /// OAuth cua ASP.NET Core, vi client (RN Google Sign-In) da tu lay id_token tu Google SDK.
    /// </summary>
    private async Task<GoogleUserDto?> VerifyGoogleIdTokenAsync(string idToken)
    {
        var clientId = _configuration["Authentication:Google:ClientId"];
        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = string.IsNullOrWhiteSpace(clientId) ? null : new[] { clientId }
        };

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Google id_token validation failed");
            return null;
        }

        if (string.IsNullOrWhiteSpace(payload.Subject))
            return null;

        return new GoogleUserDto
        {
            IDSocial = payload.Subject,
            Email = payload.Email ?? string.Empty,
            Name = payload.Name ?? string.Empty,
            Picture = payload.Picture ?? string.Empty,
            VerifiedEmail = payload.EmailVerified
        };
    }

    private async Task<GoogleUserDto> ReadGoogleProfileAsync(AuthenticateResult externalAuth)
    {
        var accessToken = externalAuth.Properties?.GetTokenValue("access_token");
        if (string.IsNullOrEmpty(accessToken))
            return MapGoogleFromClaims(externalAuth.Principal!);

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, GoogleUserInfoUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            // Userinfo tu choi (token het han, thieu scope) — claim tu id_token van du dung.
            _logger.LogWarning("Google userinfo returned {Status}; falling back to id_token claims.", (int)response.StatusCode);
            return MapGoogleFromClaims(externalAuth.Principal!);
        }

        var el = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        return new GoogleUserDto
        {
            IDSocial = ReadString(el, "sub"),
            Email = ReadString(el, "email"),
            Name = ReadString(el, "name"),
            Picture = ReadString(el, "picture"),
            VerifiedEmail = el.TryGetProperty("email_verified", out var ev)
                            && ev.ValueKind == JsonValueKind.True
        };
    }

    private static GoogleUserDto MapGoogleFromClaims(ClaimsPrincipal principal) => new()
    {
        IDSocial = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
        Email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
        Name = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
        Picture = principal.FindFirstValue("picture") ?? string.Empty,
        VerifiedEmail = bool.TryParse(principal.FindFirstValue("email_verified"), out var v) && v
    };

    // ─── Facebook ─────────────────────────────────────────────────────────────

    public async Task<OAuthResult> CompleteFacebookLoginAsync(AuthenticateResult externalAuth)
    {
        if (externalAuth?.Principal is null || !externalAuth.Succeeded)
            return OAuthResult.Fail("Xac thuc Facebook chua hoan tat.");

        try
        {
            var dto = await ReadFacebookProfileAsync(externalAuth);

            if (string.IsNullOrWhiteSpace(dto.IDSocial))
                return OAuthResult.Fail("Khong doc duoc thong tin tai khoan Facebook.");

            var (user, error) = await ResolveSocialUserAsync(
                dto.IDSocial,
                dto.Email,
                "facebook",
                // Facebook Graph API chi tra ve truong "email" cho tai khoan da xac thuc email —
                // dto.Email khac null/rong o day dong nghia voi da verified, nen luon true.
                emailVerified: true,
                () => UserMapper.FromFacebook(dto));

            return user is null ? OAuthResult.Fail(error!) : await IssueTokensAsync(user, DeviceInfo.Resolve(null, null, null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Facebook OAuth failed");
            return OAuthResult.Fail("Da xay ra loi khi dang nhap Facebook.");
        }
    }

    /// <summary>Dang nhap Facebook tu access token do SDK native (React Native FBSDK) gui len truc tiep.</summary>
    public async Task<OAuthResult> CompleteFacebookTokenLoginAsync(string accessToken, DeviceInfo device)
    {
        try
        {
            if (!await VerifyFacebookAccessTokenAsync(accessToken))
                return OAuthResult.Fail("Facebook token khong hop le hoac khong thuoc ung dung nay.");

            var dto = await FetchFacebookProfileAsync(accessToken);
            if (string.IsNullOrWhiteSpace(dto.IDSocial))
                return OAuthResult.Fail("Khong doc duoc thong tin tai khoan Facebook.");

            var (user, error) = await ResolveSocialUserAsync(
                dto.IDSocial,
                dto.Email,
                "facebook",
                emailVerified: true,
                () => UserMapper.FromFacebook(dto));

            return user is null ? OAuthResult.Fail(error!) : await IssueTokensAsync(user, device);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Facebook native token login failed");
            return OAuthResult.Fail("Da xay ra loi khi dang nhap Facebook.");
        }
    }

    /// <summary>
    /// Xac nhan access token do client tu lay (RN FBSDK) thuc su thuoc ve app nay va con hieu
    /// luc, qua debug_token cua Facebook — khac voi luong popup, o do token da di qua middleware
    /// OAuth cua ASP.NET Core nen mac dinh da dung app. App access token (app_id|app_secret)
    /// khong bao gio roi khoi server.
    /// </summary>
    private async Task<bool> VerifyFacebookAccessTokenAsync(string accessToken)
    {
        var appId = _configuration["Authentication:Facebook:AppId"];
        var appSecret = _configuration["Authentication:Facebook:AppSecret"];
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
        {
            _logger.LogWarning("Facebook AppId/AppSecret chua cau hinh — tu choi native token login.");
            return false;
        }

        var client = _httpClientFactory.CreateClient();
        var url = "https://graph.facebook.com/debug_token" +
                   $"?input_token={Uri.EscapeDataString(accessToken)}" +
                   $"&access_token={Uri.EscapeDataString(appId)}%7C{Uri.EscapeDataString(appSecret)}";

        using var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Facebook debug_token returned {Status}.", (int)response.StatusCode);
            return false;
        }

        var el = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        if (!el.TryGetProperty("data", out var data))
            return false;

        var isValid = data.TryGetProperty("is_valid", out var v) && v.ValueKind == JsonValueKind.True;
        var tokenAppId = ReadString(data, "app_id");
        return isValid && string.Equals(tokenAppId, appId, StringComparison.Ordinal);
    }

    private async Task<FacebookUserDto> ReadFacebookProfileAsync(AuthenticateResult externalAuth)
    {
        var accessToken = externalAuth.Properties?.GetTokenValue("access_token");
        if (string.IsNullOrEmpty(accessToken))
            return MapFacebookFromClaims(externalAuth.Principal!);

        return await FetchFacebookProfileAsync(accessToken, () => MapFacebookFromClaims(externalAuth.Principal!));
    }

    private async Task<FacebookUserDto> FetchFacebookProfileAsync(string accessToken, Func<FacebookUserDto>? onFailure = null)
    {
        var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(FacebookGraphUrl + Uri.EscapeDataString(accessToken));
        if (!response.IsSuccessStatusCode)
        {
            if (onFailure is null)
                throw new InvalidOperationException($"Facebook Graph returned {(int)response.StatusCode}.");

            _logger.LogWarning("Facebook Graph returned {Status}; falling back to claims.", (int)response.StatusCode);
            return onFailure();
        }

        var info = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        return new FacebookUserDto
        {
            IDSocial = ReadString(info, "id"),
            Name = ReadString(info, "name"),
            Email = info.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null,
            Picture = ReadNested(info, "picture", "data", "url"),
            Birthday = ReadString(info, "birthday"),
            Gender = info.TryGetProperty("gender", out var g) ? g.GetString() ?? "Other" : "Other",
            Location = ReadNested(info, "location", "name"),
            Hometown = ReadNested(info, "hometown", "name")
        };
    }

    private static FacebookUserDto MapFacebookFromClaims(ClaimsPrincipal principal) => new()
    {
        IDSocial = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
        Name = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
        Email = principal.FindFirstValue(ClaimTypes.Email),
        Picture = principal.FindFirstValue("picture") ?? string.Empty
    };

    // ─── Dung chung ───────────────────────────────────────────────────────────

    /// <summary>
    /// Tim user theo social id; chua co thi tim theo email de LIEN KET vao tai khoan da co
    /// san, chua co nua moi tao moi. Chi tu dong lien ket khi (a) nha cung cap da xac thuc
    /// email — tranh chiem tai khoan qua mot email chua verify — va (b) tai khoan hien tai
    /// CHUA tung lien ket social nao (IDSocial null) — tranh am tham ghi de mot lien ket
    /// khac (vd Facebook) ma nguoi dung khong hay biet, vi schema hien chi giu duoc mot
    /// social id moi luc. Cac truong hop con lai van tu choi nhu truoc.
    /// </summary>
    private async Task<(UserDb? User, string? Error)> ResolveSocialUserAsync(
        string socialId, string? email, string provider, bool emailVerified, Func<UserDb> createNew)
    {
        var existing = await _userService.GetBySocialIdAsync(socialId);
        if (existing is not null)
        {
            if (!existing.IsActive)
                return (null, "Tai khoan da bi vo hieu hoa.");

            var refreshed = await _userService.RecordLoginAttemptAsync(
                existing.UserUUID, loginAttempts: 0, isLocked: false, lockUntil: null, lastLogin: DateTime.UtcNow);

            return (refreshed ?? existing, null);
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var byEmail = await _userService.GetByUsernameOrEmailAsync(email);
            if (byEmail is not null)
            {
                if (emailVerified && string.IsNullOrEmpty(byEmail.IDSocial))
                {
                    if (!byEmail.IsActive)
                        return (null, "Tai khoan da bi vo hieu hoa.");

                    var linked = await _userService.LinkSocialAsync(byEmail.UserUUID, socialId, provider);
                    if (linked is not null)
                    {
                        var refreshed = await _userService.RecordLoginAttemptAsync(
                            linked.UserUUID, loginAttempts: 0, isLocked: false, lockUntil: null, lastLogin: DateTime.UtcNow);
                        return (refreshed ?? linked, null);
                    }
                }

                return (null, "Email nay da duoc dang ky bang phuong thuc khac. Vui long dang nhap bang mat khau.");
            }
        }

        try
        {
            return (await _userService.CreateUserAsync(createNew()), null);
        }
        catch (InvalidOperationException ex)
        {
            // Thuong la trung username sinh tu email — hiem, nhung phai bao ro thay vi 500.
            _logger.LogWarning(ex, "Could not create {Provider} user for socialId={SocialId}", provider, socialId);
            return (null, "Khong tao duoc tai khoan tu thong tin nha cung cap. Vui long thu lai.");
        }
    }

    private async Task<OAuthResult> IssueTokensAsync(UserDb user, DeviceInfo device)
    {
        return new OAuthResult
        {
            Success = true,
            Token = await _accessTokenService.CreateForCustomerAsync(user, device),
            RefreshToken = await _refreshTokenService.GenerateAsync(user.UserCode, Roles.CUSTOMER, device),
            ExpiresIn = _accessTokenService.AccessTokenLifetimeSeconds,
            UserData = UserMapper.ToLoginResponse(user)
        };
    }

    private static string ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string ReadNested(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
                return string.Empty;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : string.Empty;
    }
}
