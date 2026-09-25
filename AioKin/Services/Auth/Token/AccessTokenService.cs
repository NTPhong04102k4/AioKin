using System.Security.Cryptography;
using System.Text;
using AioKin.Common;
using AioKin.Services.Common.Cache;
using StaffDb = AioKin.Data.Entities.Security.Staff;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Services.Auth.Token;

public class AccessTokenService : IAccessTokenService
{
    private readonly IRedisService _redis;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccessTokenService> _logger;

    /// <summary>So access token song song toi da cho mot subject — cung nguong voi refresh token.</summary>
    private const int MaxSessionsPerSubject = 10;

    private const char SessionSeparator = '\n';

    public AccessTokenService(IRedisService redis, IConfiguration configuration, ILogger<AccessTokenService> logger)
    {
        _redis = redis;
        _configuration = configuration;
        _logger = logger;
    }

    public int AccessTokenLifetimeSeconds => JwtConfiguration.ResolveAccessTokenMinutes(_configuration) * 60;

    public Task<string> CreateForCustomerAsync(UserDb user)
    {
        var fullName = $"{user.FirstName} {user.LastName}".Trim();

        return IssueAsync(new AccessTokenSession
        {
            Kind = AccessTokenSubjectKind.Customer,
            Subject = user.UserCode,
            UserUuid = user.UserUUID,
            Username = user.Username,
            Email = user.Email,
            Name = string.IsNullOrWhiteSpace(fullName) ? user.Username : fullName,
            Role = Roles.CUSTOMER
        });
    }

    public Task<string> CreateForStaffAsync(StaffDb staff, string roleName)
        => IssueAsync(new AccessTokenSession
        {
            Kind = AccessTokenSubjectKind.Staff,
            Subject = staff.Username,
            StaffId = staff.StaffID,
            Username = staff.Username,
            Email = staff.Email,
            Name = staff.FullName,
            Role = roleName
        });

    private async Task<string> IssueAsync(AccessTokenSession session)
    {
        // 32 byte ngau nhien, base64url khong padding — cung cach RefreshTokenService dang sinh.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var hash = HashToken(token);
        var ttl = TimeSpan.FromSeconds(AccessTokenLifetimeSeconds);

        await _redis.SetAsync(RedisKeys.AccessSession(hash), session, ttl);
        await TrackSessionAsync(session.Subject, hash, ttl);

        _logger.LogInformation("Access token issued for subject={Subject}, kind={Kind}", session.Subject, session.Kind);
        return token;
    }

    private async Task TrackSessionAsync(string subject, string hash, TimeSpan ttl)
    {
        var key = RedisKeys.UserAccessSessions(subject);
        var existing = await _redis.GetStringAsync(key) ?? string.Empty;
        var hashes = existing.Split(SessionSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        hashes.Add(hash);

        if (hashes.Count > MaxSessionsPerSubject)
        {
            // Token bi day ra khoi danh sach cung phai bi thu hoi, neu khong no van dung
            // duoc cho toi khi het TTL ma khong con cach nao revoke.
            foreach (var evicted in hashes[..^MaxSessionsPerSubject])
                await _redis.DeleteAsync(RedisKeys.AccessSession(evicted));

            hashes = hashes[^MaxSessionsPerSubject..];
        }

        await _redis.SetStringAsync(key, string.Join(SessionSeparator, hashes), ttl);
    }

    public Task<AccessTokenSession?> ValidateAsync(string token)
        => _redis.GetAsync<AccessTokenSession>(RedisKeys.AccessSession(HashToken(token)));

    public async Task RevokeAsync(string token)
    {
        await _redis.DeleteAsync(RedisKeys.AccessSession(HashToken(token)));
        _logger.LogInformation("Access token revoked");
    }

    public async Task RevokeAllForSubjectAsync(string subject)
    {
        var key = RedisKeys.UserAccessSessions(subject);
        var existing = await _redis.GetStringAsync(key) ?? string.Empty;

        foreach (var hash in existing.Split(SessionSeparator, StringSplitOptions.RemoveEmptyEntries))
            await _redis.DeleteAsync(RedisKeys.AccessSession(hash));

        await _redis.DeleteAsync(key);
        _logger.LogInformation("All access sessions revoked for subject={Subject}", subject);
    }

    /// <summary>
    /// Token that khong bao gio nam trong Redis lam key: mot ban dump/backup Redis khong
    /// duoc de lo session dang song duoi dang dung duoc luon.
    /// </summary>
    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
