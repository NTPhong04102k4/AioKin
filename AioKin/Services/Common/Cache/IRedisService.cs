namespace AioKin.Services.Common.Cache;

/// <summary>
/// Truu tuong hoa Redis de phan con lai cua ung dung khong phu thuoc vao cach ket noi:
/// TCP (StackExchange), REST (Upstash) hay IMemoryCache khi chay local. Moi thao tac
/// deu nuot loi ha tang va tra ve gia tri an toan — mot su co cache khong duoc bien
/// thanh 500 cho nguoi dung.
/// </summary>
public interface IRedisService
{
    /// <summary>Luu object duoi dang JSON, ghi de neu key da ton tai.</summary>
    Task<bool> SetAsync<T>(string key, T value, TimeSpan expiry);

    /// <summary>Doc object. Tra null neu key khong ton tai hoac da het han.</summary>
    Task<T?> GetAsync<T>(string key);

    /// <summary>Luu chuoi tho.</summary>
    Task<bool> SetStringAsync(string key, string value, TimeSpan expiry);

    /// <summary>
    /// SET ... EX ... NX — chi ghi khi key chua ton tai, nguyen tu o phia server.
    /// Dung lam khoa chong trung; check-then-set thuong van ho race khi hai request
    /// toi cung luc.
    /// </summary>
    Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiry);

    /// <summary>Doc chuoi tho. Tra null neu khong ton tai.</summary>
    Task<string?> GetStringAsync(string key);

    /// <summary>Xoa key. True neu key ton tai va da bi xoa.</summary>
    Task<bool> DeleteAsync(string key);

    /// <summary>Xoa moi key co cung tien to. Tra ve so key da xoa.</summary>
    Task<long> DeleteByPrefixAsync(string keyPrefix);

    /// <summary>Kiem tra key co ton tai khong.</summary>
    Task<bool> ExistsAsync(string key);

    /// <summary>Gia han TTL cho key da ton tai.</summary>
    Task<bool> ExtendTtlAsync(string key, TimeSpan expiry);

    /// <summary>TTL con lai cua key, null neu key khong ton tai hoac khong co han.</summary>
    Task<TimeSpan?> GetTtlAsync(string key);
}
