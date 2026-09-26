using System.Net;
using System.Net.Http.Json;
using AioKin.Common;
using AioKin.Models.ViewModel.Auth.User;
using AioKin.Services.Auth.Token;
using AioKin.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AioKin.Tests.Auth;

/// <summary>
/// P18: /auth/login khong duoc phep 400 khi client bo trong ca ba truong thiet bi — phan lon
/// client (vd Android hien tai) chua gui gi ca, va phai van dang nhap duoc voi mot thiet bi
/// "unknown" duoc server tu sinh, chu khong phai boc loi validation.
/// </summary>
[Collection(ApiCollection.Name)]
public class LoginDeviceHttpTests
{
    private readonly ApiFixture _fixture;

    public LoginDeviceHttpTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Login_khong_gui_truong_thiet_bi_van_thanh_cong_va_duoc_gan_thiet_bi_unknown()
    {
        const string password = "Test@12345";
        PasswordHelper.CreatePasswordHash(password, out var hash, out var salt);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var client = _fixture.CreateClient();

        // Body khong co DeviceId/DeviceName/Platform — dung dung cach client cu/Android hien
        // tai dang goi /auth/login.
        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            usernameOrPhoneOrEmail = user.Username,
            password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body!.AccessToken));

        var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var session = await tokens.ValidateAsync(body.AccessToken);

        Assert.NotNull(session);
        Assert.NotNull(session!.DeviceId);
        Assert.StartsWith("unknown-", session.DeviceId);
        Assert.Null(session.DeviceName);
        Assert.Null(session.Platform);
    }
}
