using System.Security.Claims;
using System.Text.Encodings.Web;
using AioKin.Common;
using AioKin.Services.Auth.Token;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AioKin.Middleware;

/// <summary>
/// Thay AddJwtBearer: doc opaque access token tu header Authorization, tra cuu session
/// trong Redis qua IAccessTokenService, roi dung ClaimsPrincipal thu cong. Khong con chu ky
/// de verify — "hop le" nghia la "con ton tai va chua het TTL trong Redis", nen
/// JwtBlacklistMiddleware khong con can thiet: thu hoi la xoa key, co hieu luc ngay tai day.
/// </summary>
public class OpaqueAccessTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Giu nguyen ten "Bearer" de AddSecurityDefinition("Bearer", ...) trong Swagger
    /// va moi [Authorize] hien co khong phai doi gi.</summary>
    public const string SchemeName = "Bearer";

    private readonly IAccessTokenService _accessTokenService;

    public OpaqueAccessTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IAccessTokenService accessTokenService)
        : base(options, logger, encoder)
    {
        _accessTokenService = accessTokenService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0)
            return AuthenticateResult.NoResult();

        var session = await _accessTokenService.ValidateAsync(token);
        if (session is null)
            return AuthenticateResult.Fail("Access token khong hop le hoac da het han.");

        var identity = new ClaimsIdentity(BuildClaims(session, token), SchemeName, AioKinClaims.Username, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    private static List<Claim> BuildClaims(AccessTokenSession session, string rawToken)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, session.Role),
            new(AioKinClaims.Username, session.Username),
            new(AioKinClaims.SessionToken, rawToken)
        };

        if (session.Kind == AccessTokenSubjectKind.Customer)
        {
            claims.Add(new Claim(AioKinClaims.UserCode, session.Subject));
            if (session.UserUuid is { } uuid)
                claims.Add(new Claim(ClaimTypes.NameIdentifier, uuid.ToString()));
        }

        if (session.StaffId is { } staffId)
            claims.Add(new Claim(AioKinClaims.StaffId, staffId.ToString()));

        if (!string.IsNullOrWhiteSpace(session.Email))
            claims.Add(new Claim(ClaimTypes.Email, session.Email));

        return claims;
    }
}
