using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AioKin.Common;
using Microsoft.IdentityModel.Tokens;
using StaffDb = AioKin.Data.Entities.Security.Staff;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Services.Auth.Token;

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;

        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Thieu Jwt:Key — khong the ky access token.");

        // HMAC-SHA256 can khoa toi thieu 256 bit. Kiem tra ngay luc khoi dong thay vi de
        // request dau tien nem ra loi kho hieu tu thu vien.
        if (Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Jwt:Key phai dai it nhat 32 byte (256 bit) cho HMAC-SHA256.");

        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);
    }

    public int AccessTokenLifetimeSeconds => JwtConfiguration.ResolveAccessTokenMinutes(_configuration) * 60;

    public string CreateForCustomer(UserDb user)
    {
        var claims = BaseClaims(Roles.CUSTOMER);

        // sub = UserUUID (id cong khai), khong phai UserID noi bo.
        claims.Add(new Claim(JwtRegisteredClaimNames.Sub, user.UserUUID.ToString()));
        claims.Add(new Claim(AioKinClaims.UserCode, user.UserCode));
        claims.Add(new Claim(AioKinClaims.Username, user.Username));

        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));

        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        claims.Add(new Claim(JwtRegisteredClaimNames.Name, string.IsNullOrWhiteSpace(fullName) ? user.Username : fullName));

        return Write(claims);
    }

    public string CreateForStaff(StaffDb staff, string roleName)
    {
        var claims = BaseClaims(roleName);

        claims.Add(new Claim(JwtRegisteredClaimNames.Sub, staff.StaffID.ToString()));
        claims.Add(new Claim(AioKinClaims.StaffId, staff.StaffID.ToString()));
        claims.Add(new Claim(AioKinClaims.Username, staff.Username));
        claims.Add(new Claim(JwtRegisteredClaimNames.Email, staff.Email));
        claims.Add(new Claim(JwtRegisteredClaimNames.Name, staff.FullName));

        return Write(claims);
    }

    private static List<Claim> BaseClaims(string role) =>
    [
        // jti la thu ma middleware blacklist tra cuu khi logout — moi token phai co mot cai rieng.
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim(JwtRegisteredClaimNames.Iat,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ClaimValueTypes.Integer64),
        new Claim(ClaimTypes.Role, role)
    ];

    private string Write(IEnumerable<Claim> claims)
    {
        var token = new JwtSecurityToken(
            issuer: JwtConfiguration.ResolveIssuer(_configuration),
            audience: JwtConfiguration.ResolveAudienceForSigning(_configuration),
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(JwtConfiguration.ResolveAccessTokenMinutes(_configuration)),
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
