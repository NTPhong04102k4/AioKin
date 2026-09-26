using AioKin.Common;
using AioKin.Data.Entities.Security;
using AioKin.Services.Auth.Token;
using AioKin.Services.Common.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AioKin.Tests.Auth;

/// <summary>
/// MemoryCacheRedisService thay cho Redis that: AccessTokenService khong quan tam no dang
/// noi voi ban cai nao cua IRedisService, nen khong can Postgres/ApiFixture cho cac test nay.
/// </summary>
public class AccessTokenServiceTests
{
    private static AccessTokenService CreateSut(int expiryMinutes = 60)
    {
        var redis = new MemoryCacheRedisService(new MemoryCache(new MemoryCacheOptions()), NullLogger<MemoryCacheRedisService>.Instance);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:ExpiryMinutes"] = expiryMinutes.ToString() })
            .Build();
        return new AccessTokenService(redis, config, NullLogger<AccessTokenService>.Instance);
    }

    private static User NewUser() => new()
    {
        UserCode = $"UC{Guid.NewGuid():N}"[..20],
        Username = $"u{Guid.NewGuid():N}"[..20],
        PasswordHash = "x",
        Email = "vi@example.com",
        FirstName = "Vi",
        LastName = "Nguyen"
    };

    [Fact]
    public async Task CreateForCustomerAsync_roi_ValidateAsync_tra_ve_dung_session()
    {
        var sut = CreateSut();
        var user = NewUser();

        var token = await sut.CreateForCustomerAsync(user, DeviceInfo.Unknown);
        var session = await sut.ValidateAsync(token);

        Assert.NotNull(session);
        Assert.Equal(AccessTokenSubjectKind.Customer, session!.Kind);
        Assert.Equal(user.UserCode, session.Subject);
        Assert.Equal(user.UserUUID, session.UserUuid);
        Assert.Equal(Roles.CUSTOMER, session.Role);
        Assert.Null(session.StaffId);
    }

    [Fact]
    public async Task CreateForCustomerAsync_gan_dung_thong_tin_thiet_bi()
    {
        var sut = CreateSut();
        var user = NewUser();
        var device = new DeviceInfo("device-abc", "Pixel 8", "android");

        var token = await sut.CreateForCustomerAsync(user, device);
        var session = await sut.ValidateAsync(token);

        Assert.NotNull(session);
        Assert.Equal("device-abc", session!.DeviceId);
        Assert.Equal("Pixel 8", session.DeviceName);
        Assert.Equal("android", session.Platform);
    }

    [Fact]
    public async Task CreateForStaffAsync_gan_dung_StaffId_va_role_duoc_truyen_vao()
    {
        var sut = CreateSut();
        var staff = new Staff
        {
            Username = "staff1",
            Email = "staff1@example.com",
            FullName = "Staff One",
            PasswordHash = "x",
            PasswordSalt = "x",
            StaffCode = "STF001"
        };

        var token = await sut.CreateForStaffAsync(staff, Roles.ADMIN, DeviceInfo.Unknown);
        var session = await sut.ValidateAsync(token);

        Assert.NotNull(session);
        Assert.Equal(AccessTokenSubjectKind.Staff, session!.Kind);
        Assert.Equal(staff.StaffID, session.StaffId);
        Assert.Equal(Roles.ADMIN, session.Role);
        Assert.Null(session.UserUuid);
    }

    [Fact]
    public async Task ValidateAsync_tra_null_cho_token_khong_ton_tai()
    {
        var sut = CreateSut();

        var session = await sut.ValidateAsync("token-chua-bao-gio-duoc-phat");

        Assert.Null(session);
    }

    [Fact]
    public async Task RevokeAsync_lam_token_het_hop_le_ngay()
    {
        var sut = CreateSut();
        var token = await sut.CreateForCustomerAsync(NewUser(), DeviceInfo.Unknown);

        await sut.RevokeAsync(token);
        var session = await sut.ValidateAsync(token);

        Assert.Null(session);
    }

    [Fact]
    public async Task RevokeAllForSubjectAsync_thu_hoi_moi_token_dang_song_cua_subject()
    {
        var sut = CreateSut();
        var user = NewUser();

        var tokenA = await sut.CreateForCustomerAsync(user, DeviceInfo.Unknown);
        var tokenB = await sut.CreateForCustomerAsync(user, DeviceInfo.Unknown);

        await sut.RevokeAllForSubjectAsync(user.UserCode);

        Assert.Null(await sut.ValidateAsync(tokenA));
        Assert.Null(await sut.ValidateAsync(tokenB));
    }

    [Fact]
    public async Task RevokeForDeviceAsync_chi_thu_hoi_session_cua_dung_thiet_bi()
    {
        var sut = CreateSut();
        var user = NewUser();

        var tokenDeviceA = await sut.CreateForCustomerAsync(user, new DeviceInfo("device-a", null, null));
        var tokenDeviceB = await sut.CreateForCustomerAsync(user, new DeviceInfo("device-b", null, null));

        await sut.RevokeForDeviceAsync(user.UserCode, "device-a");

        Assert.Null(await sut.ValidateAsync(tokenDeviceA));
        Assert.NotNull(await sut.ValidateAsync(tokenDeviceB));
    }

    /// <summary>
    /// Finding #1: rotation (revoke + tao moi lien tuc) cua mot thiet bi khong duoc phep day
    /// session CON SONG cua thiet bi khac ra khoi danh sach theo doi chi vi vi tri. Truoc fix,
    /// TrackSessionAsync day theo VI TRI ma khong loc hash chet truoc — hash chet cua B (da bi
    /// RevokeAsync xoa key nhung con nam trong danh sach) chiem cho, day dan A ra ngoai va
    /// A bi xoa oan du chua he dang xuat.
    /// </summary>
    [Fact]
    public async Task TrackSessionAsync_khong_day_session_con_song_cua_thiet_bi_khac_ra_ngoai_khi_thiet_bi_kia_rotate_nhieu_lan()
    {
        var sut = CreateSut();
        var user = NewUser();

        var tokenA = await sut.CreateForCustomerAsync(user, new DeviceInfo("device-a", null, null));

        // Thiet bi B rotate (nhu /auth/refresh-token that: revoke session cu roi tao session
        // moi) nhieu hon MaxSessionsPerSubject lan — moi lan de lai 1 hash CHET trong danh sach
        // vi RevokeAsync chi xoa key AccessSession, khong don danh sach nay.
        for (var i = 0; i < 12; i++)
        {
            var tokenB = await sut.CreateForCustomerAsync(user, new DeviceInfo("device-b", null, null));
            await sut.RevokeAsync(tokenB);
        }

        Assert.NotNull(await sut.ValidateAsync(tokenA));
    }

    [Fact]
    public void AccessTokenLifetimeSeconds_doc_tu_JwtExpiryMinutes()
    {
        var sut = CreateSut(expiryMinutes: 15);

        Assert.Equal(15 * 60, sut.AccessTokenLifetimeSeconds);
    }

    [Fact]
    public async Task IssueAsync_nem_loi_khi_Redis_SetAsync_that_bai_thay_vi_tra_ve_token_gia()
    {
        // Redis "chet" ngay tu buoc ghi session dau tien — truoc day IssueAsync bo qua gia tri
        // tra ve cua SetAsync nen van tra ve mot token "thanh cong" du khong con session nao
        // trong Redis, token do se 401 ngay lan dung dau tien.
        var redis = new AlwaysFailingSetRedisService();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:ExpiryMinutes"] = "60" })
            .Build();
        var sut = new AccessTokenService(redis, config, NullLogger<AccessTokenService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateForCustomerAsync(NewUser(), DeviceInfo.Unknown));
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
