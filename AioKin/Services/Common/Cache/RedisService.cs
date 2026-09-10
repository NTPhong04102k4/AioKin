using System.Text.Json;
using StackExchange.Redis;

namespace AioKin.Services.Common.Cache;

/// <summary>Ban chinh thuc: noi toi Redis qua giao thuc TCP bang StackExchange.Redis.</summary>
public class RedisService : IRedisService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly string _instanceName;
    private readonly ILogger<RedisService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RedisService(IConnectionMultiplexer redis, IConfiguration configuration, ILogger<RedisService> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _instanceName = configuration["Redis:InstanceName"] ?? "AioKin:";
        _logger = logger;
    }

    /// <summary>Them instance name vao key de nhieu ung dung dung chung mot Redis khong dam nhau.</summary>
    private string PrefixKey(string key) => $"{_instanceName}{key}";

    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan expiry)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            return await _db.StringSetAsync(PrefixKey(key), json, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis SET failed for key={Key}", key);
            return false;
        }
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var json = await _db.StringGetAsync(PrefixKey(key));
            if (json.IsNullOrEmpty) return default;
            return JsonSerializer.Deserialize<T>(json!, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis GET failed for key={Key}", key);
            return default;
        }
    }

    public async Task<bool> SetStringAsync(string key, string value, TimeSpan expiry)
    {
        try
        {
            return await _db.StringSetAsync(PrefixKey(key), value, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis SET (string) failed for key={Key}", key);
            return false;
        }
    }

    public async Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiry)
    {
        try
        {
            return await _db.StringSetAsync(PrefixKey(key), value, expiry, When.NotExists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis SET NX failed for key={Key}", key);

            // FAIL-OPEN co chu dich: tra true = "coi nhu gianh duoc khoa". Redis chet thi
            // mat bao dam chong trung, nhung nguoi dung van thao tac duoc. Tra false se
            // khien phia goi hieu la "da ton tai" va tu choi moi request — bien mot su co
            // cache thanh su co ngung dich vu.
            return true;
        }
    }

    public async Task<string?> GetStringAsync(string key)
    {
        try
        {
            var value = await _db.StringGetAsync(PrefixKey(key));
            return value.IsNullOrEmpty ? null : value.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis GET (string) failed for key={Key}", key);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string key)
    {
        try
        {
            return await _db.KeyDeleteAsync(PrefixKey(key));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis DEL failed for key={Key}", key);
            return false;
        }
    }

    public async Task<long> DeleteByPrefixAsync(string keyPrefix)
    {
        try
        {
            var prefixedPattern = $"{PrefixKey(keyPrefix)}*";
            var deleted = 0L;

            foreach (var endpoint in _redis.GetEndPoints())
            {
                var server = _redis.GetServer(endpoint);
                if (!server.IsConnected)
                    continue;

                foreach (var key in server.Keys(pattern: prefixedPattern, pageSize: 250))
                {
                    if (await _db.KeyDeleteAsync(key))
                        deleted++;
                }
            }

            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis DEL prefix failed for keyPrefix={KeyPrefix}", keyPrefix);
            return 0;
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            return await _db.KeyExistsAsync(PrefixKey(key));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis EXISTS failed for key={Key}", key);
            return false;
        }
    }

    public async Task<bool> ExtendTtlAsync(string key, TimeSpan expiry)
    {
        try
        {
            return await _db.KeyExpireAsync(PrefixKey(key), expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis EXPIRE failed for key={Key}", key);
            return false;
        }
    }

    public async Task<TimeSpan?> GetTtlAsync(string key)
    {
        try
        {
            return await _db.KeyTimeToLiveAsync(PrefixKey(key));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis TTL failed for key={Key}", key);
            return null;
        }
    }
}
