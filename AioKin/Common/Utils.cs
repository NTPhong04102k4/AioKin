using System.Security.Cryptography;
using System.Text;

namespace AioKin.Common;

public static class Utils
{
    /// <summary>SHA256 rut gon 20 ky tu hex — dung sinh StaffCode on dinh tu chuoi mo ta.</summary>
    public static string HashTo20Chars(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..20];
    }
}
