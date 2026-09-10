using System.Security.Cryptography;
using System.Text;
using AioKin.Common;
using AioKin.Services.Common.Cache;

namespace AioKin.Services.Auth.PasswordUser;

public class TemporaryPasswordService : ITemporaryPasswordService
{
    private readonly ILogger<TemporaryPasswordService> _logger;
    private readonly IRedisService _redis;

    public TemporaryPasswordService(ILogger<TemporaryPasswordService> logger, IRedisService redis)
    {
        _logger = logger;
        _redis = redis;
    }

    public async Task<string> GenerateTemporaryPasswordAsync(string email)
    {
        var tempPassword = PasswordHelper.GenerateTemporaryPassword();
        var data = new TemporaryPasswordData
        {
            Password = tempPassword,
            Email = email,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(RedisTtl.TempPassword)
        };

        var saved = await _redis.SetAsync(RedisKeys.TempPassword(email), data, RedisTtl.TempPassword);
        if (!saved)
        {
            _logger.LogError("Failed to persist temporary password for email={Email}", email);
            throw new InvalidOperationException("Khong luu duoc mat khau tam. Vui long thu lai.");
        }

        _logger.LogInformation("Temporary password generated for email={Email}", email);
        return tempPassword;
    }

    public async Task<bool> VerifyTemporaryPasswordAsync(string email, string tempPassword)
    {
        var key = RedisKeys.TempPassword(email);
        var data = await _redis.GetAsync<TemporaryPasswordData>(key);
        if (data is null)
        {
            _logger.LogWarning("Temporary password not found for email={Email}", email);
            return false;
        }

        if (DateTime.UtcNow > data.ExpiresAt)
        {
            _logger.LogWarning("Temporary password expired for email={Email}", email);
            await _redis.DeleteAsync(key);
            return false;
        }

        if (CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(data.Password),
                Encoding.UTF8.GetBytes(tempPassword)))
        {
            await _redis.DeleteAsync(key);
            _logger.LogInformation("Temporary password verified for email={Email}", email);
            return true;
        }

        _logger.LogWarning("Invalid temporary password for email={Email}", email);
        return false;
    }

    public async Task<bool> IsTemporaryPasswordExpiredAsync(string email)
    {
        var data = await _redis.GetAsync<TemporaryPasswordData>(RedisKeys.TempPassword(email));
        return data is null || DateTime.UtcNow > data.ExpiresAt;
    }
}
