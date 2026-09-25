using System.Security.Cryptography;
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

    public Task<string> CreateForCustomerAsync(UserDb user, DeviceInfo device)
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
            Role = Roles.CUSTOMER,
            DeviceId = device.DeviceId,
            DeviceName = device.DeviceName,
            Platform = device.Platform
        });
    }

    public Task<string> CreateForStaffAsync(StaffDb staff, string roleName, DeviceInfo device)
        => IssueAsync(new AccessTokenSession
        {
            Kind = AccessTokenSubjectKind.Staff,
            Subject = staff.Username,
            StaffId = staff.StaffID,
            Username = staff.Username,
            Email = staff.Email,
            Name = staff.FullName,
            Role = roleName,
            DeviceId = device.DeviceId,
            DeviceName = device.DeviceName,
            Platform = device.Platform
        });

    private async Task<string> IssueAsync(AccessTokenSession session)
    {
        // 32 byte ngau nhien, base64url khong padding — cung cach RefreshTokenService dang sinh.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var hash = TokenHash.Sha256Hex(token);
        var ttl = TimeSpan.FromSeconds(AccessTokenLifetimeSeconds);

        var saved = await _redis.SetAsync(RedisKeys.AccessSession(hash), session, ttl);
        if (!saved)
        {
            // Cung pattern voi OtpService/TemporaryPasswordService: khong duoc tra ve token
            // "thanh cong" ma phia sau khong con session nao trong Redis — token do se 401
            // ngay khi dung, gay nham lan hon la bao loi luon tai day.
            _logger.LogError("Failed to persist access session to Redis for subject={Subject}, kind={Kind}",
                session.Subject, session.Kind);
            throw new InvalidOperationException("Khong tao duoc access token. Vui long thu lai.");
        }

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
        => _redis.GetAsync<AccessTokenSession>(RedisKeys.AccessSession(TokenHash.Sha256Hex(token)));

    public async Task RevokeAsync(string token)
        => await RevokeByHashAsync(TokenHash.Sha256Hex(token));

    public async Task RevokeByHashAsync(string hash)
    {
        var deleted = await _redis.DeleteAsync(RedisKeys.AccessSession(hash));
        if (!deleted)
            _logger.LogWarning("Access session key not found on revoke (already expired or revoked)");

        _logger.LogInformation("Access token revoked");
    }

    public async Task RevokeAllForSubjectAsync(string subject)
    {
        var key = RedisKeys.UserAccessSessions(subject);
        var existing = await _redis.GetStringAsync(key) ?? string.Empty;

        foreach (var hash in existing.Split(SessionSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!await _redis.DeleteAsync(RedisKeys.AccessSession(hash)))
                _logger.LogWarning("Access session key not found while revoking all for subject={Subject}", subject);
        }

        await _redis.DeleteAsync(key);
        _logger.LogInformation("All access sessions revoked for subject={Subject}", subject);
    }

    public async Task<IReadOnlyList<(string Id, AccessTokenSession Session)>> ListSessionsAsync(string subject)
    {
        var key = RedisKeys.UserAccessSessions(subject);
        var existing = await _redis.GetStringAsync(key) ?? string.Empty;
        var hashes = existing.Split(SessionSeparator, StringSplitOptions.RemoveEmptyEntries);

        var result = new List<(string Id, AccessTokenSession Session)>();
        var alive = new List<string>();

        foreach (var hash in hashes)
        {
            var session = await _redis.GetAsync<AccessTokenSession>(RedisKeys.AccessSession(hash));
            if (session is null)
                continue; // session het han/da bi thu hoi noi khac — bo khoi danh sach theo doi luon (P10)

            alive.Add(hash);
            result.Add((TokenHash.PublicId(hash), session));
        }

        // Chi ghi lai Redis khi thuc su co hash chet can don — tranh ghi lai moi lan goi
        // GET /account/sessions ma khong doi gi.
        if (alive.Count != hashes.Length)
            await _redis.SetStringAsync(key, string.Join(SessionSeparator, alive), TimeSpan.FromSeconds(AccessTokenLifetimeSeconds));

        return result;
    }

    public async Task<AccessTokenSession?> RevokeByIdAsync(string subject, string id)
    {
        // Id sai dinh dang khong the khop bat ky hash that nao (public id luon la 12 hex tu
        // sha256), nhung tu choi tu day tranh lam viec Redis vo ich voi input ro rang khong
        // hop le — va van tra ve cung "khong tim thay" nhu moi truong hop khac.
        if (!TokenHash.IsValidPublicId(id))
            return null;

        var key = RedisKeys.UserAccessSessions(subject);
        var existing = await _redis.GetStringAsync(key) ?? string.Empty;
        var hashes = existing.Split(SessionSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();

        // So khop bang dang thuc (khong phai StartsWith): id cong khai luon dung 12 ky tu,
        // va day la tap da duoc gioi han san trong danh sach cua chinh subject nay — khong
        // co request nao doc duoc id thuoc ve nguoi khac di qua day.
        var match = hashes.FirstOrDefault(h => TokenHash.PublicId(h) == id);
        if (match is null)
            return null;

        var session = await _redis.GetAsync<AccessTokenSession>(RedisKeys.AccessSession(match));

        // Bo khoi danh sach theo doi trong moi truong hop: da thu hoi thanh cong hoac hash
        // nay tu lau da chet (session het han) — ca hai deu khong con ly do gi de giu lai.
        hashes.Remove(match);
        await _redis.SetStringAsync(key, string.Join(SessionSeparator, hashes), TimeSpan.FromSeconds(AccessTokenLifetimeSeconds));

        if (session is null)
        {
            _logger.LogWarning("RevokeByIdAsync: id khop hash nhung session da het han, subject={Subject}", subject);
            return null;
        }

        await _redis.DeleteAsync(RedisKeys.AccessSession(match));
        _logger.LogInformation("Access session revoked by id for subject={Subject}", subject);
        return session;
    }

    public async Task RevokeForDeviceAsync(string subject, string deviceId)
    {
        var key = RedisKeys.UserAccessSessions(subject);
        var existing = await _redis.GetStringAsync(key) ?? string.Empty;

        // Hash con lai trong danh sach sau khi bi thu hoi o day se tu that bai o
        // ValidateAsync (key AccessSession da bi xoa) va duoc don dan qua MaxSessionsPerSubject
        // hoac RevokeAllForSubjectAsync ke tiep — cung pattern voi
        // RefreshTokenService.RevokeAllForDeviceAsync, khong doc lai toan bo danh sach chi de
        // loai bo mot hash.
        foreach (var hash in existing.Split(SessionSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var session = await _redis.GetAsync<AccessTokenSession>(RedisKeys.AccessSession(hash));
            if (session is not null && string.Equals(session.DeviceId, deviceId, StringComparison.Ordinal))
                await _redis.DeleteAsync(RedisKeys.AccessSession(hash));
        }

        _logger.LogInformation("Access sessions revoked for subject={Subject}, device={DeviceId}", subject, deviceId);
    }
}
