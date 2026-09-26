using System.Security.Cryptography;
using System.Text;

namespace AioKin.Common;

/// <summary>
/// Bam token truoc khi dung lam Redis key, cho ca access va refresh token: mot ban
/// dump/backup Redis khong duoc de lo session dang song duoi dang dung duoc luon.
/// Cung la noi duy nhat sinh public session id (12 hex dau cua hash) — dung o
/// /account/sessions de nguoi dung chon thu hoi ma khong lo raw token.
/// </summary>
public static class TokenHash
{
    private const int PublicIdLength = 12;

    public static string Sha256Hex(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>
    /// Id cong khai (an toan de hien thi/gui ve client) suy tu hash — 12 ky tu hex dau.
    /// Khong the doi nguoc lai token, nhung du ngan de nguoi dung phan biet cac phien.
    /// </summary>
    public static string PublicId(string hash) => hash[..PublicIdLength];

    /// <summary>
    /// True neu <paramref name="id"/> co dung hinh dang mot public id (12 ky tu hex thuong) —
    /// dung de tu choi ngay id sai dinh dang truoc khi do vao Redis, khong lo them thong tin
    /// gi qua thoi gian phan hoi.
    /// </summary>
    public static bool IsValidPublicId(string id)
        => id.Length == PublicIdLength && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
