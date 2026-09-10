using System.Security.Cryptography;
using AioKin.Common;
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

    public async Task<string> GenerateAsync(string userCode, string role)
    {
        // 64 byte ngau nhien, base64url khong padding — an toan khi dat trong URL/header.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var ttl = ResolveTtl();
        await _redis.SetStringAsync(RedisKeys.RefreshToken(token), $"{userCode}|{role}", ttl);

        // Giu danh sach token cua user de RevokeAll co gi ma duyet. Luu dang chuoi ngan
        // cach bang xuong dong de dung duoc tren ca Redis TCP lan REST.
        var userTokensKey = RedisKeys.UserRefreshTokens(userCode);
        var existing = await _redis.GetStringAsync(userTokensKey) ?? string.Empty;
        var tokens = existing.Split(TokenSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        tokens.Add(token);

        if (tokens.Count > MaxTokensPerUser)
        {
            // Token bi day ra khoi danh sach cung phai bi thu hoi, neu khong no van dung
            // duoc cho toi khi het TTL ma khong con cach nao revoke.
            foreach (var evicted in tokens[..^MaxTokensPerUser])
                await _redis.DeleteAsync(RedisKeys.RefreshToken(evicted));

            tokens = tokens[^MaxTokensPerUser..];
        }

        await _redis.SetStringAsync(userTokensKey, string.Join(TokenSeparator, tokens), ttl);

        _logger.LogInformation("Refresh token generated for user={UserCode}", userCode);
        return token;
    }

    public async Task<(string UserCode, string Role)?> ValidateAsync(string token)
    {
        var payload = await _redis.GetStringAsync(RedisKeys.RefreshToken(token));
        if (string.IsNullOrEmpty(payload))
        {
            _logger.LogWarning("Refresh token not found or expired");
            return null;
        }

        var parts = payload.Split('|');
        if (parts.Length != 2)
        {
            _logger.LogWarning("Refresh token payload malformed");
            return null;
        }

        return (parts[0], parts[1]);
    }

    public async Task RevokeAsync(string token)
    {
        await _redis.DeleteAsync(RedisKeys.RefreshToken(token));
        _logger.LogInformation("Refresh token revoked");
    }

    public async Task RevokeAllAsync(string userCode)
    {
        var userTokensKey = RedisKeys.UserRefreshTokens(userCode);
        var existing = await _redis.GetStringAsync(userTokensKey) ?? string.Empty;

        foreach (var token in existing.Split(TokenSeparator, StringSplitOptions.RemoveEmptyEntries))
            await _redis.DeleteAsync(RedisKeys.RefreshToken(token));

        await _redis.DeleteAsync(userTokensKey);
        _logger.LogInformation("All refresh tokens revoked for user={UserCode}", userCode);
    }
}
