using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace AioKin.Services.Common.Cache;

/// <summary>
/// <see cref="IRedisService"/> chay tren IMemoryCache — dung khi may dev khong co Redis.
/// Khong ho tro scan theo tien to (tra 0), nhung du cho OTP, refresh token va rate limit
/// trong mot tien trinh duy nhat.
/// </summary>
public class MemoryCacheRedisService : IRedisService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryCacheRedisService> _logger;
    private readonly Lock _nxLock = new();
    private readonly Lock _deleteLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public MemoryCacheRedisService(IMemoryCache cache, ILogger<MemoryCacheRedisService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<bool> SetAsync<T>(string key, T value, TimeSpan expiry)
    {
        _cache.Set(key, JsonSerializer.Serialize(value, JsonOptions), expiry);
        return Task.FromResult(true);
    }

    public Task<T?> GetAsync<T>(string key)
    {
        if (!_cache.TryGetValue(key, out string? json) || json is null)
            return Task.FromResult<T?>(default);

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<T>(json, JsonOptions));
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "MemoryCache deserialize failed for key={Key}", key);
            return Task.FromResult<T?>(default);
        }
    }

    public Task<bool> SetStringAsync(string key, string value, TimeSpan expiry)
    {
        _cache.Set(key, value, expiry);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Chi nguyen tu trong pham vi mot tien trinh. Chay nhieu instance ma khong co Redis
    /// that thi hai instance deu co the "gianh duoc" khoa — chap nhan duoc vi day la ban
    /// du phong cho moi truong dev.
    /// </summary>
    public Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiry)
    {
        lock (_nxLock)
        {
            if (_cache.TryGetValue(key, out _))
                return Task.FromResult(false);

            _cache.Set(key, value, expiry);
            return Task.FromResult(true);
        }
    }

    public Task<string?> GetStringAsync(string key)
    {
        _cache.TryGetValue(key, out string? value);
        return Task.FromResult(value);
    }

    /// <summary>
    /// P5: phai tra ve dung "key co ton tai va da bi xoa hay khong", giong RedisService/
    /// UpstashRedisRestService — khong duoc luon tra true. BiometricAuthService.VerifyAsync
    /// dung gia tri nay de dam bao challenge chi duoc tieu thu boi DUNG MOT request khi hai
    /// request verify chay song song tren cung mot challengeId.
    ///
    /// Fix round 1: TryGetValue + Remove phai la MOT khoi nguyen tu, khong phai hai buoc roi
    /// rac — neu khong, hai DeleteAsync goi song song tren cung mot key co the ca hai cung
    /// thay existed=true TRUOC khi ben nao Remove, ca hai cung tra ve true (dung lai dieu ma
    /// fix ban dau dinh chan). IMemoryCache.Remove khong tra ve gia tri "co xoa duoc khong",
    /// nen phai tu gate bang lock — cung kieu voi SetIfNotExistsAsync o tren.
    /// </summary>
    public Task<bool> DeleteAsync(string key)
    {
        lock (_deleteLock)
        {
            var existed = _cache.TryGetValue(key, out _);
            if (existed)
                _cache.Remove(key);

            return Task.FromResult(existed);
        }
    }

    public Task<long> DeleteByPrefixAsync(string keyPrefix)
    {
        _logger.LogWarning("MemoryCacheRedisService: DeleteByPrefix khong duoc ho tro (prefix={Prefix})", keyPrefix);
        return Task.FromResult(0L);
    }

    public Task<bool> ExistsAsync(string key)
        => Task.FromResult(_cache.TryGetValue(key, out _));

    public Task<bool> ExtendTtlAsync(string key, TimeSpan expiry)
    {
        if (!_cache.TryGetValue(key, out object? value))
            return Task.FromResult(false);

        _cache.Set(key, value!, expiry);
        return Task.FromResult(true);
    }

    public Task<TimeSpan?> GetTtlAsync(string key)
        => Task.FromResult<TimeSpan?>(null);
}
