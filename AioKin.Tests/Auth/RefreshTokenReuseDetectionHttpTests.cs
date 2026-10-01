using System.Net;
using System.Net.Http.Json;
using AioKin.Common;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Auth.User;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.Token;
using AioKin.Services.Common.Cache;
using AioKin.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AioKin.Tests.Auth;

/// <summary>
/// Kiem tra end-to-end qua HTTP cho hai lop vang vua them vao /auth/refresh-token: phat hien
/// tai su dung (token reuse detection, dua tren TokenFamilyId) va tran tren tuyet doi cua phien
/// (AbsoluteExpiresAt) — xem RefreshTokenServiceTests cho test o muc service.
/// </summary>
[Collection(ApiCollection.Name)]
public class RefreshTokenReuseDetectionHttpTests
{
    private readonly ApiFixture _fixture;

    public RefreshTokenReuseDetectionHttpTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Refresh_lai_mot_token_da_bi_xoay_vong_bi_tu_choi_va_giet_toan_bo_chuoi()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var accessTokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var refreshTokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var device = new DeviceInfo("dev-X-id", "dev-X", "android");

        var access1 = await accessTokens.CreateForCustomerAsync(user, device);
        var refresh1 = await refreshTokens.GenerateAsync(user.UserCode, Roles.CUSTOMER, device);

        var client = _fixture.CreateClient();

        // Thiet bi that xoay vong binh thuong: refresh1 -> refresh2.
        var firstRefresh = await client.PostAsJsonAsync("/auth/refresh-token", new { refreshToken = refresh1 });
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);
        var tokens2 = await firstRefresh.Content.ReadFromJsonAsync<TokenResponse>();

        // Ke tan cong (hoac chinh thiet bi do loi mang retry) dem refresh1 (da bi tombstone)
        // dung lai — day la mot lan TAI SU DUNG.
        var replay = await client.PostAsJsonAsync("/auth/refresh-token", new { refreshToken = refresh1 });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        var replayBody = await replay.Content.ReadFromJsonAsync<OperationResult>();
        Assert.Equal("TokenReuseDetected", replayBody!.ErrorCode);

        // Hau qua: ca refresh2 (dang con "hop le" truoc khi bi replay phat hien) cung phai bi
        // giet theo vi cung TokenFamilyId — khong con cach nao refresh tiep duoc chuoi nay.
        var afterAttack = await client.PostAsJsonAsync("/auth/refresh-token", new { refreshToken = tokens2!.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterAttack.StatusCode);

        // Access token cua thiet bi nay cung bi thu hoi luon, khong doi den khi TTL tu nhien.
        Assert.Null(await accessTokens.ValidateAsync(access1));
    }

    [Fact]
    public async Task Refresh_token_khac_thiet_bi_khac_family_khong_bi_anh_huong_boi_vu_tai_su_dung()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var refreshTokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var deviceA = new DeviceInfo("dev-A-id", "dev-A", "android");
        var deviceB = new DeviceInfo("dev-B-id", "dev-B", "ios");

        var refreshA = await refreshTokens.GenerateAsync(user.UserCode, Roles.CUSTOMER, deviceA);
        var refreshB = await refreshTokens.GenerateAsync(user.UserCode, Roles.CUSTOMER, deviceB);

        var client = _fixture.CreateClient();

        // Xoay vong roi replay token cu CHI tren thiet bi A.
        await client.PostAsJsonAsync("/auth/refresh-token", new { refreshToken = refreshA });
        var replayA = await client.PostAsJsonAsync("/auth/refresh-token", new { refreshToken = refreshA });
        Assert.Equal(HttpStatusCode.Unauthorized, replayA.StatusCode);

        // Thiet bi B hoan toan khong lien quan — refresh binh thuong van phai thanh cong.
        var refreshBResponse = await client.PostAsJsonAsync("/auth/refresh-token", new { refreshToken = refreshB });
        Assert.Equal(HttpStatusCode.OK, refreshBResponse.StatusCode);
    }

    /// <summary>
    /// Canh hiem: sliding TTL con hieu luc trong Redis nhung AbsoluteExpiresAt da qua (vd lech
    /// dong ho, hoac request den dung luc TTL tu nhien chua kip xoa key). GenerateAsync that
    /// trong production luon ghim ttl &lt;= tran tuyet doi nen canh nay gan nhu khong xay ra tu
    /// nhien — ghi de thang Redis de mo phong va kiem tra lop vang nay hoat dong dung.
    /// </summary>
    [Fact]
    public async Task Refresh_bi_tu_choi_khi_da_cham_tran_tuyet_doi_du_sliding_TTL_con_hieu_luc()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var redis = scope.ServiceProvider.GetRequiredService<IRedisService>();
        var refreshTokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var device = new DeviceInfo("dev-X-id", "dev-X", "android");

        var token = await refreshTokens.GenerateAsync(user.UserCode, Roles.CUSTOMER, device);
        var payload = await refreshTokens.ValidateAsync(token);
        await redis.SetAsync(
            RedisKeys.RefreshToken(TokenHash.Sha256Hex(token)),
            payload! with { AbsoluteExpiresAt = DateTime.UtcNow.AddSeconds(-1) },
            TimeSpan.FromDays(1));

        var client = _fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/refresh-token", new { refreshToken = token });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResult>();
        Assert.Equal("SessionExpired", body!.ErrorCode);

        // Token da tombstone sau khi bi tu choi — khong the dung lai lan nua du replay.
        var again = await client.PostAsJsonAsync("/auth/refresh-token", new { refreshToken = token });
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }
}
