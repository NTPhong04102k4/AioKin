using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading;
using AioKin.Data.Entities.Security;
using AioKin.Models.ViewModel.Auth.Biometric;
using AioKin.Services.Auth.Token;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Auth;

/// <summary>
/// Finding 1 (final review, SECURITY, overrides P20): logout-all va DELETE
/// /account/sessions/{id} cung phai thu hoi credential sinh trac cua CUNG thiet bi — mot
/// access/refresh token bi lo ma tu dang ky duoc sinh trac cho thiet bi do khong duoc song sot
/// qua hai duong nay (P20 truoc do chi phu duong doi/dat lai mat khau — Task 5).
///
/// P11 (nhu BiometricAuthTests/BiometricRevocationOnPasswordChangeTests): moi HttpClient dung
/// cho challenge/verify phai co X-Forwarded-For rieng, prefix 10.66.x.x — khac voi prefix cac
/// file test khac trong cung collection "api" de khong dung chung quota rate limit.
/// </summary>
[Collection(ApiCollection.Name)]
public class BiometricRevocationOnSessionRevokeTests
{
    private readonly ApiFixture _fixture;
    private static int _ipCounter;

    public BiometricRevocationOnSessionRevokeTests(ApiFixture fixture) => _fixture = fixture;

    private static (string PublicKeyBase64, ECDsa Key) NewKeyPair()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), key);
    }

    private static string Sign(ECDsa key, string nonceBase64)
        => Convert.ToBase64String(key.SignData(
            Convert.FromBase64String(nonceBase64), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    private HttpClient NewAnonymousClient()
    {
        var n = Interlocked.Increment(ref _ipCounter);
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.66.{(n >> 8) & 255}.{n & 255}");
        return client;
    }

    /// <summary>Dang ky mot thiet bi cho mot user moi (giong helper cua BiometricAuthTests) —
    /// TestUser phai duoc cap token CHO DUNG deviceId se dang ky (P6).</summary>
    private async Task<(TestUser User, string UserCode, string DeviceId, ECDsa Key)> RegisterDeviceAsync()
    {
        var (publicKey, key) = NewKeyPair();
        var deviceId = $"device-{Guid.NewGuid():N}";
        var testUser = await TestUser.CreateAsync(_fixture, new DeviceInfo(deviceId, "Test Phone", "android"));

        var registerResponse = await testUser.Client.PostAsJsonAsync("/auth/biometric/register", new
        {
            deviceId,
            deviceName = "Test Phone",
            platform = "android",
            publicKey
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        return (testUser, testUser.UserCode, deviceId, key);
    }

    private async Task<DeviceCredential> ReloadCredentialAsync(string deviceId)
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        return await db.DeviceCredentials.AsNoTracking().SingleAsync(c => c.DeviceId == deviceId);
    }

    [Fact]
    public async Task LogoutAll_thu_hoi_credential_sinh_trac_da_dang_ky_cho_thiet_bi()
    {
        var (testUser, _, deviceId, _) = await RegisterDeviceAsync();

        var beforeLogoutAll = await ReloadCredentialAsync(deviceId);
        Assert.Null(beforeLogoutAll.RevokedAt);

        var logoutAllResponse = await testUser.Client.PostAsync("/auth/logout-all", null);
        Assert.Equal(HttpStatusCode.OK, logoutAllResponse.StatusCode);

        var afterLogoutAll = await ReloadCredentialAsync(deviceId);
        Assert.NotNull(afterLogoutAll.RevokedAt);
    }

    [Fact]
    public async Task DeleteSession_thu_hoi_credential_sinh_trac_cua_cung_thiet_bi_va_verify_sau_do_that_bai()
    {
        var (testUser, userCode, deviceId, key) = await RegisterDeviceAsync();

        var sessions = await testUser.Client.GetFromJsonAsync<OperationResultOf<List<SessionListItem>>>("/account/sessions");
        var sessionId = sessions!.Data!.Single(s => s.DeviceName == "Test Phone").Id;

        var deleteResponse = await testUser.Client.DeleteAsync($"/account/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var reloaded = await ReloadCredentialAsync(deviceId);
        Assert.NotNull(reloaded.RevokedAt);

        // Bang chung hanh vi: verify sinh trac sau do phai that bai (khong con credential
        // hieu luc), khong chi kiem tra RevokedAt trong DB.
        var anonymousClient = NewAnonymousClient();
        var challengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>())!.Data!;
        var signature = Sign(key, challenge.Nonce);

        var verifyResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = challenge.ChallengeId, signature });

        var body = await verifyResponse.Content.ReadFromJsonAsync<BiometricFailureBody>();
        Assert.Equal(HttpStatusCode.Unauthorized, verifyResponse.StatusCode);
        Assert.False(body!.Success);
        Assert.Equal("InvalidCredentials", body.ErrorCode);
    }

    /// <summary>Thiet bi CHUA tung dang ky sinh trac van phai xoa session thanh cong (no-op) —
    /// khong duoc lam hong request xoa session chi vi khong co credential nao de thu hoi.</summary>
    [Fact]
    public async Task DeleteSession_khong_co_credential_sinh_trac_van_thanh_cong_no_op()
    {
        var testUser = await TestUser.CreateAsync(_fixture, new DeviceInfo($"device-{Guid.NewGuid():N}", "No Bio Phone", "ios"));

        var sessions = await testUser.Client.GetFromJsonAsync<OperationResultOf<List<SessionListItem>>>("/account/sessions");
        var sessionId = sessions!.Data![0].Id;

        var deleteResponse = await testUser.Client.DeleteAsync($"/account/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
    }
}
