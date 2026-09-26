using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using AioKin.Common;
using AioKin.Data.Entities.Security;
using AioKin.Services.Auth.PasswordUser;
using AioKin.Services.Auth.Token;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AioKin.Tests.Auth;

/// <summary>
/// Task 5 / P20: doi mat khau va dat lai mat khau phai thu hoi TOAN BO credential sinh trac
/// cua user (RevokeAllForUserAsync) — khong chi mot thiet bi. logout-all va DELETE
/// /account/sessions/{id} KHONG dung toi day (P20), nen file nay chi kiem tra hai duong
/// change-password va reset-password.
///
/// P11 (nhu BiometricAuthTests): change-password va reset-password deu "auth-strict"
/// (5/phut), va ApiFixture dung chung MOT app cho ca collection "api" nen bo dem rate limit
/// dung chung giua moi test/file. Moi HttpClient o day phai co X-Forwarded-For rieng — khong
/// thi cac test nay se dung vao quota cua test khac trong cung collection (va nguoc lai).
/// </summary>
[Collection(ApiCollection.Name)]
public class BiometricRevocationOnPasswordChangeTests
{
    private readonly ApiFixture _fixture;
    private static int _ipCounter;

    public BiometricRevocationOnPasswordChangeTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Prefix rieng (10.55.x.x) khac voi BiometricAuthTests (10.x.x.x tu 0) de khong
    /// bao gio trung IP voi file test khac trong cung collection.</summary>
    private HttpClient NewIsolatedClient()
    {
        var n = Interlocked.Increment(ref _ipCounter);
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.55.{(n >> 8) & 255}.{n & 255}");
        return client;
    }

    /// <summary>Tao truc tiep mot dong DeviceCredential con hieu luc trong DB — khong di qua
    /// endpoint /auth/biometric/register de tranh phai lam dung P6 (deviceId khop phien) chi
    /// de dung mot du lieu dau vao cho test nay.</summary>
    private static DeviceCredential NewCredential(Guid userId, string deviceId) => new()
    {
        UserID = userId,
        DeviceId = deviceId,
        DeviceName = "Test Phone",
        Platform = "android",
        PublicKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
    };

    [Fact]
    public async Task Doi_mat_khau_thu_hoi_credential_sinh_trac_cua_chinh_user()
    {
        PasswordHelper.CreatePasswordHash("OldPassw0rd!", out var hash, out var salt);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        db.Users.Add(user);

        var deviceId = $"device-{Guid.NewGuid():N}";
        var credential = NewCredential(user.UserID, deviceId);
        db.DeviceCredentials.Add(credential);
        await db.SaveChangesAsync();

        var testUser = await TestUserWithPassword(user);

        var changePassword = await testUser.Client.PostAsJsonAsync("/account/me/change-password", new
        {
            currentPassword = "OldPassw0rd!",
            newPassword = "MatKhauMoi123!"
        });
        Assert.Equal(HttpStatusCode.OK, changePassword.StatusCode);

        using var verifyScope = _fixture.CreateScope();
        var verifyDb = ApiFixture.Db(verifyScope);
        var reloaded = await verifyDb.DeviceCredentials
            .AsNoTracking()
            .SingleAsync(c => c.DeviceCredentialID == credential.DeviceCredentialID);
        Assert.NotNull(reloaded.RevokedAt);
    }

    [Fact]
    public async Task Doi_mat_khau_cua_mot_user_khong_dung_toi_credential_cua_user_khac()
    {
        PasswordHelper.CreatePasswordHash("OldPassw0rd!", out var hashA, out var saltA);
        PasswordHelper.CreatePasswordHash("KhacHoanToan1!", out var hashB, out var saltB);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var userA = TestData.NewUser();
        userA.PasswordHash = hashA;
        userA.PasswordSalt = saltA;
        var userB = TestData.NewUser();
        userB.PasswordHash = hashB;
        userB.PasswordSalt = saltB;
        db.Users.AddRange(userA, userB);

        var credentialA = NewCredential(userA.UserID, $"device-{Guid.NewGuid():N}");
        var credentialB = NewCredential(userB.UserID, $"device-{Guid.NewGuid():N}");
        db.DeviceCredentials.AddRange(credentialA, credentialB);
        await db.SaveChangesAsync();

        var testUserA = await TestUserWithPassword(userA);

        var changePassword = await testUserA.Client.PostAsJsonAsync("/account/me/change-password", new
        {
            currentPassword = "OldPassw0rd!",
            newPassword = "MatKhauMoi123!"
        });
        Assert.Equal(HttpStatusCode.OK, changePassword.StatusCode);

        using var verifyScope = _fixture.CreateScope();
        var verifyDb = ApiFixture.Db(verifyScope);
        var reloadedA = await verifyDb.DeviceCredentials.AsNoTracking()
            .SingleAsync(c => c.DeviceCredentialID == credentialA.DeviceCredentialID);
        var reloadedB = await verifyDb.DeviceCredentials.AsNoTracking()
            .SingleAsync(c => c.DeviceCredentialID == credentialB.DeviceCredentialID);

        Assert.NotNull(reloadedA.RevokedAt);
        Assert.Null(reloadedB.RevokedAt);
    }

    [Fact]
    public async Task Dat_lai_mat_khau_thu_hoi_credential_sinh_trac_cua_chinh_user()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        user.Email = $"{Guid.NewGuid():N}@test.local";
        db.Users.Add(user);

        var deviceId = $"device-{Guid.NewGuid():N}";
        var credential = NewCredential(user.UserID, deviceId);
        db.DeviceCredentials.Add(credential);
        await db.SaveChangesAsync();

        var tempPasswordService = scope.ServiceProvider.GetRequiredService<ITemporaryPasswordService>();
        var tempPassword = await tempPasswordService.GenerateTemporaryPasswordAsync(user.Email);

        var client = NewIsolatedClient();
        var resetResponse = await client.PostAsJsonAsync("/auth/reset-password", new
        {
            email = user.Email,
            temporaryPassword = tempPassword,
            newPassword = "MatKhauMoi123!"
        });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        using var verifyScope = _fixture.CreateScope();
        var verifyDb = ApiFixture.Db(verifyScope);
        var reloaded = await verifyDb.DeviceCredentials.AsNoTracking()
            .SingleAsync(c => c.DeviceCredentialID == credential.DeviceCredentialID);
        Assert.NotNull(reloaded.RevokedAt);
    }

    /// <summary>Doi mat khau khi user KHONG co credential sinh trac nao van phai thanh cong
    /// binh thuong — RevokeAllForUserAsync khong duoc nem loi tren tap rong.</summary>
    [Fact]
    public async Task Doi_mat_khau_khi_khong_co_credential_sinh_trac_nao_van_thanh_cong()
    {
        PasswordHelper.CreatePasswordHash("OldPassw0rd!", out var hash, out var salt);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var testUser = await TestUserWithPassword(user);

        var changePassword = await testUser.Client.PostAsJsonAsync("/account/me/change-password", new
        {
            currentPassword = "OldPassw0rd!",
            newPassword = "MatKhauMoi123!"
        });

        Assert.Equal(HttpStatusCode.OK, changePassword.StatusCode);
    }

    /// <summary>Cap token cho mot user da co san trong DB (voi PasswordHash that) — khac
    /// TestUser.CreateAsync vi user o day can duoc tao TRUOC (cung DeviceCredential) trong
    /// cung mot scope/SaveChanges, thay vi TestUser tu tao user moi.</summary>
    private async Task<(HttpClient Client, string UserCode)> TestUserWithPassword(User user)
    {
        using var scope = _fixture.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var accessToken = await tokens.CreateForCustomerAsync(user, DeviceInfo.Unknown);

        var client = NewIsolatedClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return (client, user.UserCode);
    }
}
