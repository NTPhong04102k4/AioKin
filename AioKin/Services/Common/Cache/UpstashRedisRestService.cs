using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AioKin.Services.Common.Cache;

/// <summary>
/// <see cref="IRedisService"/> qua REST API cua Upstash. Can thiet khi host chan ket noi
/// TCP ra ngoai (nhieu nen tang serverless), noi ma StackExchange.Redis khong bat tay duoc.
/// </summary>
public class UpstashRedisRestService : IRedisService
{
    private readonly HttpClient _httpClient;
    private readonly string _instanceName;
    private readonly ILogger<UpstashRedisRestService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public UpstashRedisRestService(HttpClient httpClient, IConfiguration configuration, ILogger<UpstashRedisRestService> logger)
    {
        _httpClient = httpClient;
        _instanceName = configuration["Redis:InstanceName"] ?? "AioKin:";
        _logger = logger;

        var (restUrl, token) = ResolveRestConfig(configuration);
        if (string.IsNullOrWhiteSpace(restUrl) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Thieu cau hinh Upstash Redis REST (UPSTASH_REDIS_REST_URL / UPSTASH_REDIS_REST_TOKEN).");

        _httpClient.BaseAddress = new Uri(restUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromMilliseconds(1500);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Doc cau hinh REST. Neu chi co chuoi ket noi kieu <c>rediss://user:token@host</c>
    /// thi suy ra URL REST va token tu chinh chuoi do.
    /// </summary>
    private static (string? RestUrl, string? Token) ResolveRestConfig(IConfiguration configuration)
    {
        var restUrl = configuration["UPSTASH_REDIS_REST_URL"] ?? configuration["Redis:RestUrl"];
        var token = configuration["UPSTASH_REDIS_REST_TOKEN"] ?? configuration["Redis:RestToken"];
        if (!string.IsNullOrWhiteSpace(restUrl) && !string.IsNullOrWhiteSpace(token))
            return (restUrl, token);

        var tcpUrl = configuration["REDIS_URL"] ?? configuration["Redis:ConnectionString"];
        if (!Uri.TryCreate(tcpUrl?.Trim().Trim('"'), UriKind.Absolute, out var redisUri))
            return (restUrl, token);

        if (!redisUri.Scheme.Equals("redis", StringComparison.OrdinalIgnoreCase)
            && !redisUri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase))
            return (restUrl, token);

        var userInfoParts = redisUri.UserInfo.Split(':', 2);
        var derivedToken = userInfoParts.Length == 2 ? Uri.UnescapeDataString(userInfoParts[1]) : token;

        return ($"https://{redisUri.Host}", derivedToken);
    }

    public static bool CanUse(IConfiguration configuration)
    {
        var (restUrl, token) = ResolveRestConfig(configuration);
        return !string.IsNullOrWhiteSpace(restUrl) && !string.IsNullOrWhiteSpace(token);
    }

    private string PrefixKey(string key) => $"{_instanceName}{key}";

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private async Task<JsonElement?> SendAsync(HttpMethod method, string path, string? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static string? ReadStringResult(JsonElement? root)
    {
        if (root is null || !root.Value.TryGetProperty("result", out var result))
            return null;

        return result.ValueKind switch
        {
            JsonValueKind.String => result.GetString(),
            JsonValueKind.Null => null,
            _ => result.GetRawText()
        };
    }

    private static long ReadLongResult(JsonElement? root)
    {
        if (root is null || !root.Value.TryGetProperty("result", out var result))
            return 0;

        if (result.ValueKind == JsonValueKind.Number && result.TryGetInt64(out var value))
            return value;

        return long.TryParse(result.GetString(), out var parsed) ? parsed : 0;
    }

    private static bool IsOk(JsonElement? root)
        => ReadStringResult(root)?.Equals("OK", StringComparison.OrdinalIgnoreCase) == true;

    private static int ToSeconds(TimeSpan expiry) => Math.Max(1, (int)Math.Ceiling(expiry.TotalSeconds));

    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan expiry)
    {
        try
        {
            // Ghi thang raw JSON lam body. Upstash luu nguyen xi body lam value, nen mot
            // lan Serialize nua se boc them dau nhay va pha moi phep so sanh phia sau.
            var json = JsonSerializer.Serialize(value, JsonOptions);
            var result = await SendAsync(HttpMethod.Post, $"set/{Escape(PrefixKey(key))}?EX={ToSeconds(expiry)}", json);

            var ok = IsOk(result);
            if (!ok)
                _logger.LogWarning("Upstash SET did not return OK for key={Key}", key);
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upstash REST SET failed for key={Key}", key);
            return false;
        }
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var root = await SendAsync(HttpMethod.Get, $"get/{Escape(PrefixKey(key))}");
            if (root is null || !root.Value.TryGetProperty("result", out var result) || result.ValueKind == JsonValueKind.Null)
                return default;

            var rawJson = result.ValueKind == JsonValueKind.String ? result.GetString() : result.GetRawText();
            return string.IsNullOrEmpty(rawJson) ? default : JsonSerializer.Deserialize<T>(rawJson, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upstash REST GET failed for key={Key}", key);
            return default;
        }
    }

    public async Task<bool> SetStringAsync(string key, string value, TimeSpan expiry)
    {
        try
        {
            return IsOk(await SendAsync(HttpMethod.Post, $"set/{Escape(PrefixKey(key))}?EX={ToSeconds(expiry)}", value));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upstash REST SET string failed for key={Key}", key);
            return false;
        }
    }

    public async Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiry)
    {
        try
        {
            return IsOk(await SendAsync(HttpMethod.Post, $"set/{Escape(PrefixKey(key))}?EX={ToSeconds(expiry)}&NX=true", value));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upstash REST SET NX failed for key={Key}", key);
            // FAIL-OPEN co chu dich — xem chu thich trong RedisService.SetIfNotExistsAsync.
            return true;
        }
    }

    public async Task<string?> GetStringAsync(string key)
    {
        try
        {
            return ReadStringResult(await SendAsync(HttpMethod.Get, $"get/{Escape(PrefixKey(key))}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upstash REST GET string failed for key={Key}", key);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string key)
    {
        try
        {
            return ReadLongResult(await SendAsync(HttpMethod.Post, $"del/{Escape(PrefixKey(key))}")) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upstash REST DEL failed for key={Key}", key);
            return false;
        }
    }

    public async Task<long> DeleteByPrefixAsync(string keyPrefix)
    {
        try
        {
            var pattern = $"{PrefixKey(keyPrefix)}*";
            var keysRoot = await SendAsync(HttpMethod.Get, $"keys/{Escape(pattern)}");
            if (keysRoot is null
                || !keysRoot.Value.TryGetProperty("result", out var result)
                || result.ValueKind != JsonValueKind.Array)
                return 0;

            var deleted = 0L;
            foreach (var key in result.EnumerateArray())
            {
                var redisKey = key.GetString();
                if (!string.IsNullOrWhiteSpace(redisKey))
                    deleted += ReadLongResult(await SendAsync(HttpMethod.Post, $"del/{Escape(redisKey)}"));
            }

            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upstash REST DEL prefix failed for keyPrefix={KeyPrefix}", keyPrefix);
            return 0;
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            return ReadLongResult(await SendAsync(HttpMethod.Get, $"exists/{Escape(PrefixKey(key))}")) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upstash REST EXISTS failed for key={Key}", key);
            return false;
        }
    }

    public async Task<bool> ExtendTtlAsync(string key, TimeSpan expiry)
    {
        try
        {
            return ReadLongResult(await SendAsync(HttpMethod.Post, $"expire/{Escape(PrefixKey(key))}/{ToSeconds(expiry)}")) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upstash REST EXPIRE failed for key={Key}", key);
            return false;
        }
    }

    public async Task<TimeSpan?> GetTtlAsync(string key)
    {
        try
        {
            var seconds = ReadLongResult(await SendAsync(HttpMethod.Get, $"ttl/{Escape(PrefixKey(key))}"));
            return seconds < 0 ? null : TimeSpan.FromSeconds(seconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upstash REST TTL failed for key={Key}", key);
            return null;
        }
    }
}
