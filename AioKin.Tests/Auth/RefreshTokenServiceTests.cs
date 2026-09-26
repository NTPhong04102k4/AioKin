using AioKin.Common;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.Token;
using AioKin.Services.Common.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AioKin.Tests.Auth;

/// <summary>
/// MemoryCacheRedisService thay cho Redis that: RefreshTokenService khong quan tam no dang
/// noi voi ban cai nao cua IRedisService, nen khong can Postgres/ApiFixture cho cac test nay.
/// </summary>
public class RefreshTokenServiceTests
{
    private static RefreshTokenService CreateSut(IRedisService? redis = null)
    {
        redis ??= new MemoryCacheRedisService(new MemoryCache(new MemoryCacheOptions()), NullLogger<MemoryCacheRedisService>.Instance);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        return new RefreshTokenService(redis, config, NullLogger<RefreshTokenService>.Instance);
    }

    [Fact]
    public async Task GenerateAsync_roi_ValidateAsync_tra_ve_dung_subject_role_deviceId()
    {
        var sut = CreateSut();

        var token = await sut.GenerateAsync("UC123", "Customer", new DeviceInfo("device-abc", "Pixel 8", "android"));
        var payload = await sut.ValidateAsync(token);

        Assert.NotNull(payload);
        Assert.Equal("UC123", payload!.Subject);
        Assert.Equal("Customer", payload.Role);
        Assert.Equal("device-abc", payload.DeviceId);
        Assert.Equal("Pixel 8", payload.DeviceName);
        Assert.Equal("android", payload.Platform);
    }

    [Fact]
    public async Task GenerateAsync_khong_can_deviceId()
    {
        var sut = CreateSut();

        var token = await sut.GenerateAsync("UC123", "Customer", DeviceInfo.Unknown);
        var payload = await sut.ValidateAsync(token);

        Assert.NotNull(payload);
        Assert.Null(payload!.DeviceId);
        Assert.Null(payload.DeviceName);
        Assert.Null(payload.Platform);
    }

    [Fact]
    public async Task RevokeAllForDeviceAsync_chi_thu_hoi_token_cua_dung_thiet_bi()
    {
        var sut = CreateSut();
        var tokenDeviceA = await sut.GenerateAsync("UC1", "Customer", new DeviceInfo("device-a", null, null));
        var tokenDeviceB = await sut.GenerateAsync("UC1", "Customer", new DeviceInfo("device-b", null, null));

        await sut.RevokeAllForDeviceAsync("UC1", "device-a");

        Assert.Null(await sut.ValidateAsync(tokenDeviceA));
        Assert.NotNull(await sut.ValidateAsync(tokenDeviceB));
    }

    /// <summary>
    /// Finding #1: rotation (revoke + generate lien tuc) cua mot thiet bi khong duoc phep day
    /// token CON SONG cua thiet bi khac ra khoi danh sach theo doi chi vi vi tri. Truoc fix,
    /// TrackTokenAsync day theo VI TRI ma khong loc hash chet truoc — hash chet cua B (da bi
    /// RevokeAsync xoa key nhung con nam trong danh sach) chiem cho, day dan A ra ngoai va
    /// A bi xoa oan du chua he dang xuat.
    /// </summary>
    [Fact]
    public async Task TrackTokenAsync_khong_day_token_con_song_cua_thiet_bi_khac_ra_ngoai_khi_thiet_bi_kia_rotate_nhieu_lan()
    {
        var sut = CreateSut();
        var tokenA = await sut.GenerateAsync("UC1", "Customer", new DeviceInfo("device-a", null, null));

        // Thiet bi B rotate (nhu /auth/refresh-token that: revoke token cu roi generate token
        // moi) nhieu hon MaxTokensPerUser lan — moi lan de lai 1 hash CHET trong danh sach vi
        // RevokeAsync chi xoa key RefreshToken, khong don danh sach nay.
        for (var i = 0; i < 12; i++)
        {
            var tokenB = await sut.GenerateAsync("UC1", "Customer", new DeviceInfo("device-b", null, null));
            await sut.RevokeAsync(tokenB);
        }

        Assert.NotNull(await sut.ValidateAsync(tokenA));
    }

    [Fact]
    public async Task RevokeAllAsync_thu_hoi_moi_thiet_bi()
    {
        var sut = CreateSut();
        var tokenA = await sut.GenerateAsync("UC1", "Customer", new DeviceInfo("device-a", null, null));
        var tokenB = await sut.GenerateAsync("UC1", "Customer", new DeviceInfo("device-b", null, null));

        await sut.RevokeAllAsync("UC1");

        Assert.Null(await sut.ValidateAsync(tokenA));
        Assert.Null(await sut.ValidateAsync(tokenB));
    }

    [Fact]
    public async Task GenerateAsync_nem_loi_khi_Redis_SetAsync_that_bai_thay_vi_tra_ve_token_gia()
    {
        // Cung pattern voi AccessTokenService.IssueAsync (P12): Redis "chet" ngay tu buoc ghi
        // dau tien nen khong duoc tra ve mot token "thanh cong" du khong con session nao trong
        // Redis — token do se that bai ngay lan refresh dau tien thay vi bao loi luon tai day.
        var redis = new AlwaysFailingSetRedisService();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var sut = new RefreshTokenService(redis, config, NullLogger<RefreshTokenService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.GenerateAsync("UC1", "Customer", new DeviceInfo("device-a", null, null)));
    }

    [Fact]
    public async Task GenerateAsync_luu_theo_khoa_da_bam_khong_luu_theo_raw_token()
    {
        // P13: sau GenerateAsync, key Redis phai la sha256(token) — khoa dung raw token
        // truoc day (auth:refresh:{rawToken}) khong duoc phep ton tai.
        var redis = new MemoryCacheRedisService(new MemoryCache(new MemoryCacheOptions()), NullLogger<MemoryCacheRedisService>.Instance);
        var sut = CreateSut(redis);

        var token = await sut.GenerateAsync("UC1", "Customer", new DeviceInfo("device-a", null, null));
        var hash = TokenHash.Sha256Hex(token);

        Assert.False(await redis.ExistsAsync(RedisKeys.RefreshToken(token)));
        Assert.True(await redis.ExistsAsync(RedisKeys.RefreshToken(hash)));
    }

    /// <summary>IRedisService gia lap: SetAsync luon that bai (mo phong Redis down), cac thao tac
    /// khac uy quyen cho MemoryCacheRedisService that de khong phai gia lap toan bo hanh vi.</summary>
    private sealed class AlwaysFailingSetRedisService : IRedisService
    {
        private readonly MemoryCacheRedisService _inner =
            new(new MemoryCache(new MemoryCacheOptions()), NullLogger<MemoryCacheRedisService>.Instance);

        public Task<bool> SetAsync<T>(string key, T value, TimeSpan expiry) => Task.FromResult(false);
        public Task<T?> GetAsync<T>(string key) => _inner.GetAsync<T>(key);
        public Task<bool> SetStringAsync(string key, string value, TimeSpan expiry) => _inner.SetStringAsync(key, value, expiry);
        public Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan expiry) => _inner.SetIfNotExistsAsync(key, value, expiry);
        public Task<string?> GetStringAsync(string key) => _inner.GetStringAsync(key);
        public Task<bool> DeleteAsync(string key) => _inner.DeleteAsync(key);
        public Task<long> DeleteByPrefixAsync(string keyPrefix) => _inner.DeleteByPrefixAsync(keyPrefix);
        public Task<bool> ExistsAsync(string key) => _inner.ExistsAsync(key);
        public Task<bool> ExtendTtlAsync(string key, TimeSpan expiry) => _inner.ExtendTtlAsync(key, expiry);
        public Task<TimeSpan?> GetTtlAsync(string key) => _inner.GetTtlAsync(key);
    }
}
