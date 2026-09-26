using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AioKin.Common;
using AioKin.Services.Auth.Token;
using AioKin.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AioKin.Tests.Auth;

[Collection(ApiCollection.Name)]
public class AccessTokenRevocationTests
{
    private readonly ApiFixture _fixture;

    public AccessTokenRevocationTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Doi_mat_khau_lam_access_token_cu_het_hop_le_ngay()
    {
        // TestData.NewUser() tao PasswordHash = "x" tho, PasswordHelper.VerifyPassword se tu
        // choi vi khong phai hash that — dung hash that de buoc xac thuc mat khau hien tai
        // trong change-password di qua, va phep kiem tra thu hoi (buoc "after") moi la buoc
        // that su dang duoc kiem tra o day.
        PasswordHelper.CreatePasswordHash("OldPassw0rd!", out var hash, out var salt);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var accessToken = await tokens.CreateForCustomerAsync(user, DeviceInfo.Unknown);

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var before = await client.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var changePassword = await client.PostAsJsonAsync("/account/me/change-password", new
        {
            currentPassword = "OldPassw0rd!",
            newPassword = "MatKhauMoi123!"
        });
        Assert.Equal(HttpStatusCode.OK, changePassword.StatusCode);

        var after = await client.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }
}
