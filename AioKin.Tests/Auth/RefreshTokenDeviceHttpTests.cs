using System.Net;
using System.Net.Http.Json;
using AioKin.Common;
using AioKin.Models.ViewModel.Auth.User;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.Token;
using AioKin.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AioKin.Tests.Auth;

/// <summary>
/// Task 2 review carry-over: /auth/refresh-token phai roi ve deviceId da luu trong payload
/// khi client khong gui lai deviceId (native/Expo thuong khong gui) — khong duoc sinh mot
/// thiet bi "ma" moi moi lan refresh, va khong duoc dung gi den thiet bi khac cua cung user.
///
/// Cap token dau vao (dev-X, dev-Y) thang qua service thay vi /auth/login that: cai dang
/// can kiem tra o day la /auth/refresh-token, va login nam sau rate limit rieng
/// (EnableRateLimiting("auth-strict")) dung chung quota voi cac test HTTP khac trong cung
/// mot ApiFixture. /auth/refresh-token (policy "auth", noi hon nhieu) van duoc goi that.
/// </summary>
[Collection(ApiCollection.Name)]
public class RefreshTokenDeviceHttpTests
{
    private readonly ApiFixture _fixture;

    public RefreshTokenDeviceHttpTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Refresh_khong_gui_deviceId_thi_roi_ve_deviceId_da_luu_va_khong_dung_thiet_bi_khac()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var accessTokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var refreshTokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        var deviceX = new DeviceInfo("dev-X-id", "dev-X", "android");
        var deviceY = new DeviceInfo("dev-Y-id", "dev-Y", "ios");

        var accessX = await accessTokens.CreateForCustomerAsync(user, deviceX);
        var refreshX = await refreshTokens.GenerateAsync(user.UserCode, Roles.CUSTOMER, deviceX);

        var accessY = await accessTokens.CreateForCustomerAsync(user, deviceY);
        var refreshY = await refreshTokens.GenerateAsync(user.UserCode, Roles.CUSTOMER, deviceY);

        var client = _fixture.CreateClient();

        // Refresh KHONG gui deviceId — dung tinh huong client native chua cap nhat.
        var refreshResponse = await client.PostAsJsonAsync("/auth/refresh-token", new { refreshToken = refreshX });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var newTokenX = await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>();

        // Access token cu cua dev-X het hieu luc ngay sau refresh (RevokeForDeviceAsync).
        Assert.Null(await accessTokens.ValidateAsync(accessX));

        // Session moi phai giu dung deviceId cua dev-X — lay tu payload refresh token, khong
        // phai mot unknown-<guid> moi sinh.
        var newSession = await accessTokens.ValidateAsync(newTokenX!.AccessToken);
        Assert.NotNull(newSession);
        Assert.Equal("dev-X-id", newSession!.DeviceId);

        // dev-Y hoan toan khong bi dung den.
        var sessionY = await accessTokens.ValidateAsync(accessY);
        Assert.NotNull(sessionY);
        Assert.Equal("dev-Y-id", sessionY!.DeviceId);
    }

    /// <summary>
    /// Finding #5: device resolution phai la PAYLOAD-FIRST. Neu uu tien truong deviceId trong
    /// body request (nhu truoc fix), ke dang giu refresh token cua dev-X co the tu xung minh la
    /// dev-Y bang cach gui deviceId cua dev-Y trong body — session moi se mang deviceId cua
    /// dev-Y, va lan RevokeForDeviceAsync/RevokeAllForDeviceAsync ke tiep tren "dev-Y" (vd
    /// DELETE /account/sessions cua chinh dev-Y that) se giet nham phien that cua dev-Y.
    /// </summary>
    [Fact]
    public async Task Refresh_gui_deviceId_khac_trong_body_khong_tu_gan_lai_thanh_thiet_bi_khac()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var accessTokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var refreshTokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        var deviceX = new DeviceInfo("dev-X-id", "dev-X", "android");
        var deviceY = new DeviceInfo("dev-Y-id", "dev-Y", "ios");

        var refreshX = await refreshTokens.GenerateAsync(user.UserCode, Roles.CUSTOMER, deviceX);
        var accessY = await accessTokens.CreateForCustomerAsync(user, deviceY);

        var client = _fixture.CreateClient();

        // Giu refresh token cua dev-X nhung gui kem deviceId cua dev-Y trong body — mo phong
        // client bi loi hoac ke tan cong co refresh token nhung khong biet/khong quan tam
        // deviceId that.
        var refreshResponse = await client.PostAsJsonAsync("/auth/refresh-token", new
        {
            refreshToken = refreshX,
            deviceId = "dev-Y-id",
            deviceName = "attacker-device",
            platform = "attacker"
        });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var newTokenX = await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>();

        // Session moi phai giu dung deviceId cua dev-X tu payload — bo qua hoan toan deviceId
        // "dev-Y-id" ma request co gang gui kem.
        var newSession = await accessTokens.ValidateAsync(newTokenX!.AccessToken);
        Assert.NotNull(newSession);
        Assert.Equal("dev-X-id", newSession!.DeviceId);

        // dev-Y hoan toan khong bi dung den du request co gui deviceId cua no.
        var sessionY = await accessTokens.ValidateAsync(accessY);
        Assert.NotNull(sessionY);
        Assert.Equal("dev-Y-id", sessionY!.DeviceId);
    }
}
