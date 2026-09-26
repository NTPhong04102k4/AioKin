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
/// GET /account/sessions va DELETE /account/sessions/{id}: liet ke + thu hoi tu xa mot
/// thiet bi cu the, khong anh huong nguoi khac hay thiet bi khac cua chinh chu.
/// </summary>
[Collection(ApiCollection.Name)]
public class SessionManagementTests
{
    private readonly ApiFixture _fixture;

    public SessionManagementTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetSessions_liet_ke_dung_1_phien_va_danh_dau_isCurrent()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var response = await testUser.Client.GetAsync("/account/sessions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<List<SessionListItem>>>();
        Assert.NotNull(body);
        var sessions = body!.Data!;
        Assert.Single(sessions);
        Assert.True(sessions[0].IsCurrent);
    }

    /// <summary>P15/G2: /account/me phai tra ve UserCode — client can gia tri nay de goi cac
    /// endpoint khac (vd danh tinh hien thi), khong chi UserID (Guid noi bo).</summary>
    [Fact]
    public async Task GetMe_tra_ve_UserCode_cua_chinh_chu()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var response = await testUser.Client.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<LoginResponse>>();
        Assert.NotNull(body);
        Assert.Equal(testUser.UserCode, body!.Data!.UserCode);
    }

    [Fact]
    public async Task DeleteSession_thu_hoi_access_token_va_khong_anh_huong_nguoi_khac()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var stranger = await TestUser.CreateAsync(_fixture);

        var listResponse = await owner.Client.GetFromJsonAsync<OperationResultOf<List<SessionListItem>>>("/account/sessions");
        var sessionId = listResponse!.Data![0].Id;

        // Nguoi la khong xoa duoc session cua owner — phai tra 404, khong duoc lam gi ca.
        var forbidden = await stranger.Client.DeleteAsync($"/account/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        var stillWorks = await owner.Client.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);

        var deleted = await owner.Client.DeleteAsync($"/account/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        var afterDelete = await owner.Client.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterDelete.StatusCode);
    }

    /// <summary>Id sai dinh dang (khong phai 12 hex thuong) khong duoc lam gi — 404 nhu id
    /// khong ton tai, khong duoc bao loi validation rieng vach ro co su khac biet.</summary>
    [Fact]
    public async Task DeleteSession_id_sai_dinh_dang_tra_404()
    {
        var owner = await TestUser.CreateAsync(_fixture);

        var response = await owner.Client.DeleteAsync("/account/sessions/khong-hop-le");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var stillWorks = await owner.Client.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    /// <summary>
    /// P4/P3/P7: xoa session cua dev-A phai keo theo thu hoi CA refresh token cua dev-A
    /// (khong the /auth/refresh-token de lay access token moi), nhung khong dung gi den
    /// dev-B cua cung mot user.
    ///
    /// Cap token thang qua service (nhu TestUser.CreateAsync) thay vi goi /auth/login that:
    /// login nam sau EnableRateLimiting("auth-strict") (5 request/phut/IP), va ApiFixture
    /// dung chung 1 app cho ca collection test — goi /auth/login nhieu lan se dung vao quota
    /// cua chinh no. /auth/refresh-token (policy "auth", 20/phut) van duoc goi that vi day
    /// la cai dang can kiem tra.
    /// </summary>
    [Fact]
    public async Task DeleteSession_thu_hoi_ca_refresh_token_cua_dung_thiet_bi_khong_dung_thiet_bi_khac()
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

        var accessB = await accessTokens.CreateForCustomerAsync(user, deviceB);
        var refreshB = await refreshTokens.GenerateAsync(user.UserCode, Roles.CUSTOMER, deviceB);

        var clientA = _fixture.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessA);

        var clientB = _fixture.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessB);

        var anonymousClient = _fixture.CreateClient();

        // Tim dung session cua dev-A trong danh sach (co ca hai thiet bi vi cung mot user).
        var sessions = await clientA.GetFromJsonAsync<OperationResultOf<List<SessionListItem>>>("/account/sessions");
        var sessionAId = sessions!.Data!.Single(s => s.DeviceName == "dev-A").Id;

        var deleteResponse = await clientA.DeleteAsync($"/account/sessions/{sessionAId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // dev-A: access token va refresh token deu khong con dung duoc.
        var meA = await clientA.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meA.StatusCode);

        // Assert dung 401 + errorCode thay vi chi NotEqual(OK): NotEqual(OK) van "pass" ca khi
        // request bi 429 (rate limiter "auth" dung chung IP/partition voi cac test HTTP khac
        // trong cung ApiFixture), 400 hay 500 — nhung khong con la dieu dang kiem tra. Tong so
        // request that toi /auth/refresh-token trong toan bo test suite (file nay + mot test
        // rieng trong RefreshTokenDeviceHttpTests) chi la 3, con xa nguong 20/phut cua policy
        // "auth" nen khong can duong tranh rate limiter rieng cho test nay.
        var refreshAResponse = await anonymousClient.PostAsJsonAsync("/auth/refresh-token", new { refreshToken = refreshA });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAResponse.StatusCode);
        var refreshABody = await refreshAResponse.Content.ReadFromJsonAsync<OperationResultOf<object>>();
        Assert.Equal("InvalidRefreshToken", refreshABody!.ErrorCode);

        // dev-B: khong bi dung gi ca.
        var meB = await clientB.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.OK, meB.StatusCode);

        var refreshBResponse = await anonymousClient.PostAsJsonAsync("/auth/refresh-token", new { refreshToken = refreshB });
        Assert.Equal(HttpStatusCode.OK, refreshBResponse.StatusCode);
    }
}

// Doi voi test doc JSON: OperationResult.Data la object, nen dung shape rieng cho deserialize
// thay vi ep kieu OperationResult that.
public class OperationResultOf<T>
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public T? Data { get; set; }
}

public class SessionListItem
{
    public string Id { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? Platform { get; set; }
    public long IssuedAt { get; set; }
    public bool IsCurrent { get; set; }
}
