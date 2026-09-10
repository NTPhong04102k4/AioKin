using System.Security.Cryptography;
using AioKin.Common;
using AioKin.Services.Common.Cache;

namespace AioKin.Services.Auth.Otp;

public class OtpService : IOtpService
{
    private readonly ILogger<OtpService> _logger;
    private readonly IRedisService _redis;

    private const int MaxAttempts = 3;

    public OtpService(ILogger<OtpService> logger, IRedisService redis)
    {
        _logger = logger;
        _redis = redis;
    }

    public async Task<string> GenerateOtpAsync(string email)
    {
        var otpCode = GenerateRandomOtp();
        var otpData = new OtpData
        {
            Code = otpCode,
            Email = email,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(RedisTtl.Otp),
            Attempts = 0,
            MaxAttempts = MaxAttempts
        };

        // Ghi de truc tiep thay vi xoa roi ghi: neu xoa truoc ma ghi that bai thi nguoi
        // dung mat ca ma cu lan ma moi.
        var saved = await _redis.SetAsync(RedisKeys.Otp(email), otpData, RedisTtl.Otp);
        if (!saved)
        {
            _logger.LogError("Failed to persist OTP to Redis for email={Email}", email);
            throw new InvalidOperationException("Khong luu duoc OTP. Vui long thu lai.");
        }

        _logger.LogInformation("OTP generated for email={Email}", email);
        return otpCode;
    }

    public async Task<bool> VerifyOtpAsync(string email, string otpCode)
    {
        var key = RedisKeys.Otp(email);
        var otpData = await _redis.GetAsync<OtpData>(key);
        if (otpData is null)
        {
            _logger.LogWarning("OTP not found for email={Email}", email);
            return false;
        }

        if (otpData.Attempts >= otpData.MaxAttempts)
        {
            _logger.LogWarning("OTP max attempts exceeded for email={Email}", email);
            await _redis.DeleteAsync(key);
            return false;
        }

        if (DateTime.UtcNow > otpData.ExpiresAt)
        {
            _logger.LogWarning("OTP expired for email={Email}", email);
            await _redis.DeleteAsync(key);
            return false;
        }

        otpData.Attempts++;

        if (CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(otpData.Code),
                System.Text.Encoding.UTF8.GetBytes(otpCode)))
        {
            await _redis.DeleteAsync(key);
            _logger.LogInformation("OTP verified for email={Email}", email);
            return true;
        }

        // Ghi lai so lan thu voi dung TTL con lai — khong duoc gia han, neu khong ke tan
        // cong co the lam moi cua so vo han bang cach doan sai lien tuc.
        var remaining = otpData.ExpiresAt - DateTime.UtcNow;
        if (remaining > TimeSpan.Zero)
            await _redis.SetAsync(key, otpData, remaining);

        _logger.LogWarning("OTP invalid for email={Email}, attempts={Attempts}/{Max}",
            email, otpData.Attempts, otpData.MaxAttempts);
        return false;
    }

    public async Task<bool> IsOtpExpiredAsync(string email)
    {
        var otpData = await _redis.GetAsync<OtpData>(RedisKeys.Otp(email));
        return otpData is null || DateTime.UtcNow > otpData.ExpiresAt;
    }

    private static string GenerateRandomOtp()
        => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}
