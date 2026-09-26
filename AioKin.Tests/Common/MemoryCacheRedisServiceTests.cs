using AioKin.Services.Common.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AioKin.Tests.Common;

/// <summary>
/// MemoryCacheRedisService la ban thay the IRedisService khi khong co Redis that (dev/test).
/// DeleteAsync phai tra ve dung "key co ton tai va da bi xoa" (P5) — bai kiem tra o day tap
/// trung vao tinh nguyen tu cua rieng thao tac nay duoi tai song song, vi
/// BiometricAuthService.VerifyAsync dua vao no de dam bao mot challenge chi duoc dung mot
/// lan ngay ca khi hai request verify chay dung luc tren cung mot challengeId.
/// </summary>
public class MemoryCacheRedisServiceTests
{
    private static MemoryCacheRedisService CreateSut()
        => new(new MemoryCache(new MemoryCacheOptions()), NullLogger<MemoryCacheRedisService>.Instance);

    [Fact]
    public async Task DeleteAsync_tren_key_ton_tai_tra_true_va_xoa_key()
    {
        var sut = CreateSut();
        await sut.SetStringAsync("k1", "v1", TimeSpan.FromMinutes(1));

        var deleted = await sut.DeleteAsync("k1");

        Assert.True(deleted);
        Assert.False(await sut.ExistsAsync("k1"));
    }

    [Fact]
    public async Task DeleteAsync_tren_key_khong_ton_tai_tra_false()
    {
        var sut = CreateSut();

        Assert.False(await sut.DeleteAsync("khong-ton-tai"));
    }

    /// <summary>
    /// Fix round 1: TryGetValue+Remove phai la mot khoi nguyen tu. Ban ru mot lo hong that su
    /// duoi tai — ban dau (TryGetValue roi Remove, khong khoa) hai lan goi song song deu co the
    /// thay key con ton tai TRUOC khi ben nao xoa, va ca hai cung tra ve true. Chay hang tram
    /// lan goi DeleteAsync THUC SU song song (Task.WhenAll) tren CUNG mot key — dung mot lan
    /// nghia la chinh xac DUY NHAT mot lan goi duoc tra ve true, moi lan khac phai tra false.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_song_song_tren_cung_key_chi_dung_mot_lan_tra_true()
    {
        const int attempts = 200;

        for (var round = 0; round < 20; round++)
        {
            var sut = CreateSut();
            await sut.SetStringAsync("shared-key", "v1", TimeSpan.FromMinutes(1));

            var tasks = Enumerable.Range(0, attempts).Select(_ => sut.DeleteAsync("shared-key"));
            var results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(r => r));
            Assert.Equal(attempts - 1, results.Count(r => !r));
        }
    }
}
