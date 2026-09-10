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

    public Task<bool> DeleteAsync(string key)
    {
        _cache.Remove(key);
        return Task.FromResult(true);
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
