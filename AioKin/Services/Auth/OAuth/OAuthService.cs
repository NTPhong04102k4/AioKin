using System.Security.Claims;
using System.Text.Json;
using AioKin.Common;
using AioKin.Models.Transfers.ProfileUser;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.Token;
using AioKin.Services.Auth.User;
using Microsoft.AspNetCore.Authentication;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Services.Auth.OAuth;

public class OAuthService : IOAuthService
{
    private const string GoogleUserInfoUrl = "https://openidconnect.googleapis.com/v1/userinfo";
    private const string FacebookGraphUrl =
        "https://graph.facebook.com/v18.0/me?fields=id,name,email,picture,birthday,gender,location,hometown&access_token=";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserService _userService;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ILogger<OAuthService> _logger;

    public OAuthService(
        IHttpClientFactory httpClientFactory,
        IUserService userService,
        IAccessTokenService accessTokenService,
        IRefreshTokenService refreshTokenService,
        ILogger<OAuthService> logger)
    {
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
                () => UserMapper.FromGoogle(dto));

            return user is null ? OAuthResult.Fail(error!) : await IssueTokensAsync(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google OAuth failed");
            return OAuthResult.Fail("Da xay ra loi khi dang nhap Google.");
        }
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
                () => UserMapper.FromFacebook(dto));

            return user is null ? OAuthResult.Fail(error!) : await IssueTokensAsync(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Facebook OAuth failed");
            return OAuthResult.Fail("Da xay ra loi khi dang nhap Facebook.");
        }
    }

    private async Task<FacebookUserDto> ReadFacebookProfileAsync(AuthenticateResult externalAuth)
    {
        var accessToken = externalAuth.Properties?.GetTokenValue("access_token");
        if (string.IsNullOrEmpty(accessToken))
            return MapFacebookFromClaims(externalAuth.Principal!);

        var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(FacebookGraphUrl + Uri.EscapeDataString(accessToken));
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Facebook Graph returned {Status}; falling back to claims.", (int)response.StatusCode);
            return MapFacebookFromClaims(externalAuth.Principal!);
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
    /// Tim user theo social id; chua co thi tao moi. Neu email da thuoc ve mot tai khoan
    /// khac (dang ky bang mat khau, hoac SSO cua nha cung cap khac) thi tu choi thay vi
    /// gan them social id — noi tai khoan tu dong nhu vay cho phep chiem tai khoan neu
    /// nha cung cap tra ve email chua duoc xac thuc.
    /// </summary>
    private async Task<(UserDb? User, string? Error)> ResolveSocialUserAsync(
        string socialId, string? email, string provider, Func<UserDb> createNew)
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

        if (!string.IsNullOrWhiteSpace(email) && await _userService.EmailExistsAsync(email))
            return (null, "Email nay da duoc dang ky bang phuong thuc khac. Vui long dang nhap bang mat khau.");

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

    private async Task<OAuthResult> IssueTokensAsync(UserDb user)
    {
        // Popup redirect SSO khong co truong thiet bi (chua trong pham vi ke hoach nay) — van
        // sinh mot dinh danh thiet bi server-side de phien luon co the truy va thu hoi rieng.
        var device = DeviceInfo.Resolve(null, null, null);

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
