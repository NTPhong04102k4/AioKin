using System.Net;
using System.Net.Http.Json;
using AioKin.Data.Entities.Security;
using AioKin.Models.InputModel.Security;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Security;

[Collection(ApiCollection.Name)]
public class DeviceTokenTests
{
    private readonly ApiFixture _fixture;

    public DeviceTokenTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RegisterDeviceToken_ThanhCong_GhiVaoDatabase()
    {
        var user = await TestUser.CreateAsync(_fixture);

        var request = new RegisterDeviceTokenRequest
        {
            Token = "fcm-token-test-xyz-123",
            Platform = "android",
            DeviceId = "phone-dev-1"
        };

        var response = await user.Client.PostAsJsonAsync("/device/register-token", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var token = await db.DeviceTokens.FirstOrDefaultAsync(t => t.Token == request.Token);

        Assert.NotNull(token);
        Assert.Equal(user.UserId, token.UserID);
        Assert.Equal("android", token.Platform);
        Assert.Equal("phone-dev-1", token.DeviceId);
        Assert.True(token.IsActive);
    }

    [Fact]
    public async Task RegisterDeviceToken_Upsert_CapNhatKhiTokenDaTonTai()
    {
        var user = await TestUser.CreateAsync(_fixture);

        var request1 = new RegisterDeviceTokenRequest
        {
            Token = "fcm-token-repeat-test",
            Platform = "android",
            DeviceId = "device-old"
        };
        await user.Client.PostAsJsonAsync("/device/register-token", request1);

        var request2 = new RegisterDeviceTokenRequest
        {
            Token = "fcm-token-repeat-test",
            Platform = "ios",
            DeviceId = "device-new"
        };
        var response = await user.Client.PostAsJsonAsync("/device/register-token", request2);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var tokens = await db.DeviceTokens.Where(t => t.Token == "fcm-token-repeat-test").ToListAsync();

        Assert.Single(tokens);
        Assert.Equal("ios", tokens[0].Platform);
        Assert.Equal("device-new", tokens[0].DeviceId);
    }

    [Fact]
    public async Task RemoveDeviceToken_VoHieuHoaThanhCong()
    {
        var user = await TestUser.CreateAsync(_fixture);

        const string tokenStr = "fcm-token-to-deactivate";
        await user.Client.PostAsJsonAsync("/device/register-token", new RegisterDeviceTokenRequest
        {
            Token = tokenStr,
            Platform = "android"
        });

        var deleteResponse = await user.Client.DeleteAsync($"/device/token/{tokenStr}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var token = await db.DeviceTokens.FirstOrDefaultAsync(t => t.Token == tokenStr);

        Assert.NotNull(token);
        Assert.False(token.IsActive);
    }

    [Fact]
    public async Task IndexHtml_TraVeGiaoDienDangNhapChoWebView()
    {
        var client = _fixture.CreateClient();
        var response = await client.GetAsync("/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("AioKin", content);
        Assert.Contains("Tiếp tục với Google", content);
        Assert.Contains("Tiếp tục với Facebook", content);
        Assert.Contains("ReactNativeWebView", content);
    }
}
