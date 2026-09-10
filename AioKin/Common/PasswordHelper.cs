using System.Security.Cryptography;
using System.Text;

namespace AioKin.Common;

/// <summary>HMACSHA512 + salt ngau nhien cho moi mat khau. Hash/salt luu duoi dang base64.</summary>
public static class PasswordHelper
{
    public static bool VerifyPassword(string password, string? storedHashBase64, string? storedSaltBase64)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(storedHashBase64) ||
            string.IsNullOrWhiteSpace(storedSaltBase64))
        {
            return false;
        }

        try
        {
            var saltBytes = Convert.FromBase64String(storedSaltBase64);
            var storedHash = Convert.FromBase64String(storedHashBase64);

            using var hmac = new HMACSHA512(saltBytes);
            var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));

            return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
        }
        catch (FormatException)
        {
            // Du lieu seed cu co the chua hash/salt khong phai base64 — coi nhu sai mat khau.
            return false;
        }
    }

    public static void CreatePasswordHash(string password, out string hash, out string salt)
    {
        using var hmac = new HMACSHA512();
        var saltBytes = hmac.Key;
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
        hash = Convert.ToBase64String(hashBytes);
        salt = Convert.ToBase64String(saltBytes);
    }

    public static string GenerateTemporaryPassword()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var bytes = RandomNumberGenerator.GetBytes(8);

        var result = new StringBuilder(8);
        for (var i = 0; i < 8; i++)
            result.Append(chars[bytes[i] % chars.Length]);

        return result.ToString();
    }
}
