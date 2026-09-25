using AioKin.Common;
using AioKin.Services.Auth.Token;
using AioKin.Services.Common.Cache;

namespace AioKin.Services.Auth.RefreshToken;

public class RefreshTokenService : IRefreshTokenService
{
    private readonly IRedisService _redis;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RefreshTokenService> _logger;

    /// <summary>So refresh token song song toi da cho mot user — tuong ung so thiet bi.</summary>
    private const int MaxTokensPerUser = 10;

    private const char TokenSeparator = '\n';

    public RefreshTokenService(IRedisService redis, IConfiguration configuration, ILogger<RefreshTokenService> logger)
    {
        _redis = redis;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>TTL doc tu Jwt:RefreshTokenExpiryDays — fallback 7 ngay neu thieu hoac khong hop le.</summary>
    private TimeSpan ResolveTtl()
    {
        var days = int.TryParse(_configuration["Jwt:RefreshTokenExpiryDays"], out var d) && d > 0
            ? d
            : (int)RedisTtl.RefreshToken.TotalDays;
        return TimeSpan.FromDays(days);
    }

    public async Task<string> GenerateAsync(string subject, string role, DeviceInfo device)
    {
        // 64 byte ngau nhien, base64url khong padding — an toan khi dat trong URL/header.
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var hash = TokenHash.Sha256Hex(token);
        var ttl = ResolveTtl();

        var saved = await _redis.SetAsync(
            RedisKeys.RefreshToken(hash),
            new RefreshTokenPayload(subject, role, device.DeviceId, device.DeviceName, device.Platform),
            ttl);
        if (!saved)
        {
            // Cung pattern voi AccessTokenService.IssueAsync: khong duoc tra ve token
            // "thanh cong" ma phia sau khong con session nao trong Redis — token do se
            // that bai ngay khi dung, gay nham lan hon la bao loi luon tai day.
            _logger.LogError("Failed to persist refresh token to Redis for subject={Subject}, device={DeviceId}",
                subject, device.DeviceId ?? "unknown");
            throw new InvalidOperationException("Khong tao duoc refresh token. Vui long thu lai.");
        }

        await TrackTokenAsync(subject, hash, ttl);

        _logger.LogInformation("Refresh token generated for subject={Subject}, device={DeviceId}", subject, device.DeviceId ?? "unknown");
        return token;
    }

    private async Task TrackTokenAsync(string subject, string hash, TimeSpan ttl)
    {
        // Giu danh sach hash cua subject de RevokeAll co gi ma duyet. Luu dang chuoi ngan
        // cach bang xuong dong de dung duoc tren ca Redis TCP lan REST.
        var userTokensKey = RedisKeys.UserRefreshTokens(subject);
        var existing = await _redis.GetStringAsync(userTokensKey) ?? string.Empty;
        var hashes = existing.Split(TokenSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        hashes.Add(hash);

        if (hashes.Count > MaxTokensPerUser)
        {
            // Token bi day ra khoi danh sach cung phai bi thu hoi, neu khong no van dung
            // duoc cho toi khi het TTL ma khong con cach nao revoke.
            foreach (var evicted in hashes[..^MaxTokensPerUser])
                await _redis.DeleteAsync(RedisKeys.RefreshToken(evicted));

            hashes = hashes[^MaxTokensPerUser..];
        }

        await _redis.SetStringAsync(userTokensKey, string.Join(TokenSeparator, hashes), ttl);
    }

    public Task<RefreshTokenPayload?> ValidateAsync(string token)
        => _redis.GetAsync<RefreshTokenPayload>(RedisKeys.RefreshToken(TokenHash.Sha256Hex(token)));

    public async Task RevokeAsync(string token)
    {
        await _redis.DeleteAsync(RedisKeys.RefreshToken(TokenHash.Sha256Hex(token)));
        _logger.LogInformation("Refresh token revoked");
    }

    public async Task RevokeAllAsync(string subject)
    {
        var userTokensKey = RedisKeys.UserRefreshTokens(subject);
        var existing = await _redis.GetStringAsync(userTokensKey) ?? string.Empty;

        foreach (var hash in existing.Split(TokenSeparator, StringSplitOptions.RemoveEmptyEntries))
            await _redis.DeleteAsync(RedisKeys.RefreshToken(hash));

        await _redis.DeleteAsync(userTokensKey);
        _logger.LogInformation("All refresh tokens revoked for subject={Subject}", subject);
    }

    public async Task RevokeAllForDeviceAsync(string subject, string? deviceId)
    {
        var userTokensKey = RedisKeys.UserRefreshTokens(subject);
        var existing = await _redis.GetStringAsync(userTokensKey) ?? string.Empty;

        // Cac hash cua thiet bi bi thu hoi duoc de lai trong danh sach — chung se tu that
        // bai o ValidateAsync (key RefreshToken da bi xoa) va bi day ra dan qua
        // MaxTokensPerUser hoac qua lan RevokeAllAsync ke tiep. Khong dang lam sach ngay
        // vi phai doc lai tung payload chi de biet hash nao thuoc thiet bi nao.
        foreach (var hash in existing.Split(TokenSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var payload = await _redis.GetAsync<RefreshTokenPayload>(RedisKeys.RefreshToken(hash));
            if (payload is not null && string.Equals(payload.DeviceId, deviceId, StringComparison.Ordinal))
                await _redis.DeleteAsync(RedisKeys.RefreshToken(hash));
        }

        _logger.LogInformation("Refresh tokens revoked for subject={Subject}, device={DeviceId}", subject, deviceId ?? "unknown");
    }
}
