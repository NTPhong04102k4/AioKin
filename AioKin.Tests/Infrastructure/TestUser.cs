using AioKin.Common;
using AioKin.Data.Entities.Security;
using AioKin.Services.Auth.Token;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;

namespace AioKin.Tests.Infrastructure;

/// <summary>
/// Mot khach hang da dang nhap, kem HttpClient da gan token.
///
/// Tao user thang trong database va ky token bang chinh IAccessTokenService cua app, thay vi
/// di qua luong dang ky OTP that: luong do can email va Redis, va no khong phai thu dang
/// duoc kiem tra o day.
/// </summary>
public sealed class TestUser
{
    public required HttpClient Client { get; init; }
    public required Guid UserUuid { get; init; }
    public required Guid UserId { get; init; }
    public required string UserCode { get; init; }

    public static Task<TestUser> CreateAsync(ApiFixture fixture) => CreateAsync(fixture, DeviceInfo.Unknown);

    /// <summary>Cho test can mot thiet bi cu the (vd dev-A/dev-B) thay vi DeviceInfo.Unknown mac dinh.</summary>
    public static async Task<TestUser> CreateAsync(ApiFixture fixture, DeviceInfo device)
    {
        using var scope = fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var accessToken = await tokens.CreateForCustomerAsync(user, device);

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return new TestUser { Client = client, UserUuid = user.UserUUID, UserId = user.UserID, UserCode = user.UserCode };
    }

    /// <summary>
    /// Mot HttpClient THU HAI cho CUNG mot user (khong tao user moi), gan mot DeviceInfo khac —
    /// dung khi test can mo phong 2 thiet bi cua CHINH mot nguoi dung push/dong bo tranh nhau
    /// tren cung 1 prompt (vd sync engine), thay vi 2 user rieng biet.
    /// </summary>
    public async Task<HttpClient> CreateAdditionalDeviceClientAsync(ApiFixture fixture, DeviceInfo device)
    {
        using var scope = fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = await db.Users.FirstAsync(u => u.UserID == UserId);

        var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var accessToken = await tokens.CreateForCustomerAsync(user, device);

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
