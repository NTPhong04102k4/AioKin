using System.Net;
using System.Net.Http.Headers;
using AioKin.Common;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.Token;
using AioKin.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AioKin.Tests.Auth;

/// <summary>
/// Kiem tra end-to-end qua HTTP that (khong goi thang IAccessTokenService) cho /auth/logout
/// va /auth/logout-all: token con dung duoc truoc khi logout, va khong con dung duoc sau do.
/// </summary>
[Collection(ApiCollection.Name)]
public class AccessTokenLogoutHttpTests
{
    private readonly ApiFixture _fixture;

    public AccessTokenLogoutHttpTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Logout_thu_hoi_access_token_dang_dung_cua_request()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var before = await testUser.Client.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Body rong — LogoutRequest la optional, endpoint chi can revoke access token cua
        // chinh request nay du khong co refresh token di kem.
        var logout = await testUser.Client.PostAsync("/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        var after = await testUser.Client.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    /// <summary>
    /// Finding #4: Logout phai dong nghia voi DELETE /account/sessions/{id} cho chinh phien
    /// nay. Client bo trong refreshToken (vd da xoa khoi bo nho, hoac client cu chua gui) van
    /// phai lam refresh token cua CUNG thiet bi nay het hieu luc — khong thi "dang xuat" tren
    /// UI van con dang nhap lai duoc bang refresh token cu. Thiet bi khac cua cung user thi
    /// khong duoc dung den.
    /// </summary>
    [Fact]
    public async Task Logout_khong_gui_refreshToken_van_thu_hoi_refresh_token_cua_dung_thiet_bi_nay()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var accessTokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var refreshTokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        var deviceA = new DeviceInfo("dev-A-id", "dev-A", "android");
        var deviceB = new DeviceInfo("dev-B-id", "dev-B", "ios");

        var accessA = await accessTokens.CreateForCustomerAsync(user, deviceA);
        var refreshA = await refreshTokens.GenerateAsync(user.UserCode, Roles.CUSTOMER, deviceA);
        var refreshB = await refreshTokens.GenerateAsync(user.UserCode, Roles.CUSTOMER, deviceB);

        var clientA = _fixture.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessA);

        // Body rong — dung tinh huong client bo trong refreshToken.
        var logout = await clientA.PostAsync("/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        Assert.Null(await refreshTokens.ValidateAsync(refreshA));

        // dev-B hoan toan khong bi dung den.
        Assert.NotNull(await refreshTokens.ValidateAsync(refreshB));
    }

    [Fact]
    public async Task LogoutAll_thu_hoi_moi_access_token_dang_song_cua_subject()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        // Cap them mot access token thu hai cho cung user — mo phong dang nhap tu thiet bi
        // khac — de xac nhan logout-all thu hoi toan bo chu khong chi token cua request nay.
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = await db.Users.FindAsync(testUser.UserId);
        var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var secondToken = await tokens.CreateForCustomerAsync(user!, DeviceInfo.Unknown);

        var secondClient = _fixture.CreateClient();
        secondClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secondToken);

        var beforeFirst = await testUser.Client.GetAsync("/account/me");
        var beforeSecond = await secondClient.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.OK, beforeFirst.StatusCode);
        Assert.Equal(HttpStatusCode.OK, beforeSecond.StatusCode);

        var logoutAll = await testUser.Client.PostAsync("/auth/logout-all", null);
        Assert.Equal(HttpStatusCode.OK, logoutAll.StatusCode);

        var afterFirst = await testUser.Client.GetAsync("/account/me");
        var afterSecond = await secondClient.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterFirst.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterSecond.StatusCode);
    }
}
