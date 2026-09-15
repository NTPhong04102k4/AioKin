using AioKin.Common;
using AioKin.Data.Entities.Security;
using AioKin.Services.Auth.Token;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;

namespace AioKin.Tests.Infrastructure;

/// <summary>
/// Mot khach hang da dang nhap, kem HttpClient da gan token.
///
/// Tao user thang trong database va ky token bang chinh IJwtTokenService cua app, thay vi
/// di qua luong dang ky OTP that: luong do can email va Redis, va no khong phai thu dang
/// duoc kiem tra o day.
/// </summary>
public sealed class TestUser
{
    public required HttpClient Client { get; init; }
    public required Guid UserUuid { get; init; }
    public required Guid UserId { get; init; }

    public static async Task<TestUser> CreateAsync(ApiFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // CreateForCustomer khong nhan role: token cua khach hang luon mang role Customer,
        // va do la dieu IJwtTokenService tu quyet dinh chu khong phai nguoi goi.
        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var accessToken = tokens.CreateForCustomer(user);

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return new TestUser { Client = client, UserUuid = user.UserUUID, UserId = user.UserID };
    }
}
