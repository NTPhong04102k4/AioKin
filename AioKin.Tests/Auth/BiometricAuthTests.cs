using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading;
using AioKin.Models.ViewModel.Auth.Biometric;
using AioKin.Models.ViewModel.Auth.User;
using AioKin.Services.Auth.Token;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AioKin.Tests.Auth;

/// <summary>
/// /auth/biometric/{register,challenge,verify} va DELETE /auth/biometric/{deviceId}.
///
/// P11: moi HttpClient anonymous dung cho challenge/verify phai co X-Forwarded-For rieng —
/// ApiFixture dung chung MOT app cho ca collection "api" nen bo dem rate limit ("auth-strict"
/// 5/phut, "auth" 20/phut) la dung chung giua moi test trong collection; khong tach IP thi cac
/// test trong chinh file nay (va ca file khac chay truoc no) se dung vao quota cua nhau.
/// </summary>
[Collection(ApiCollection.Name)]
public class BiometricAuthTests
{
    private readonly ApiFixture _fixture;
    private static int _ipCounter;

    public BiometricAuthTests(ApiFixture fixture) => _fixture = fixture;

    private static (string PublicKeyBase64, ECDsa Key) NewKeyPair()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), key);
    }

    private static string Sign(ECDsa key, string nonceBase64)
        => Convert.ToBase64String(key.SignData(
            Convert.FromBase64String(nonceBase64), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    /// <summary>IP gia rieng cho tung client — tranh dung chung phan vung rate limit voi bat
    /// ky test nao khac trong cung collection (P11).</summary>
    private HttpClient NewAnonymousClient()
    {
        var n = Interlocked.Increment(ref _ipCounter);
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{(n >> 16) & 255}.{(n >> 8) & 255}.{n & 255}");
        return client;
    }

    /// <summary>
    /// Dang ky mot thiet bi cho mot user moi. TestUser phai duoc cap token CHO DUNG deviceId
    /// se dang ky (P6): register doi chieu request.DeviceId voi DeviceId cua chinh phien dang
    /// dung, nen khong the dung TestUser.CreateAsync mac dinh (DeviceInfo.Unknown) roi dang ky
    /// cho mot deviceId khac — se bi 403.
    /// </summary>
    private async Task<(TestUser User, string UserCode, string DeviceId)> RegisterDeviceAsync(string publicKey)
    {
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

        return (testUser, testUser.UserCode, deviceId);
    }

    private static void AssertGenericBiometricFailure(HttpStatusCode status, BiometricFailureBody? body)
    {
        // P16: moi nhanh that bai cua verify phai tra ve CUNG mot ma loi/thong diep — khong
        // duoc de client phan biet "khong co user" voi "sai chu ky" v.v qua response.
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.NotNull(body);
        Assert.False(body!.Success);
        Assert.Equal("InvalidCredentials", body.ErrorCode);
    }

    [Fact]
    public async Task Dang_ky_roi_dang_nhap_sinh_trac_thanh_cong_phat_ra_token_dung_va_dung_duoc()
    {
        var (publicKey, key) = NewKeyPair();
        var (_, userCode, deviceId) = await RegisterDeviceAsync(publicKey);
        var anonymousClient = NewAnonymousClient();

        var challengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        Assert.Equal(HttpStatusCode.OK, challengeResponse.StatusCode);
        var challengeBody = await challengeResponse.Content.ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>();
        Assert.True(challengeBody!.Success);
        var challenge = challengeBody.Data!;

        var signature = Sign(key, challenge.Nonce);
        var verifyResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = challenge.ChallengeId, signature });

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var body = await verifyResponse.Content.ReadFromJsonAsync<OperationResultOf<TokenResponse>>();
        Assert.True(body!.Success);
        Assert.False(string.IsNullOrEmpty(body.Data!.AccessToken));

        // P12: token phat ra phai la mot phien that su dung duoc, khong chi "trong duoc"
        // khoi API verify.
        using var loggedInClient = _fixture.CreateClient();
        loggedInClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body.Data.AccessToken);
        var meResponse = await loggedInClient.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    /// <summary>Response shape cua challenge sau P9 — bao trong OperationResult envelope,
    /// ChallengeId dung 32 ky tu hex (Guid "N").</summary>
    [Fact]
    public async Task Challenge_tra_ve_dung_khuon_OperationResult_voi_ChallengeId_32_hex()
    {
        var anonymousClient = NewAnonymousClient();

        var response = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge",
            new { userCode = "UC-KHONG-TON-TAI", deviceId = "device-la" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        Assert.Null(body.ErrorCode);
        Assert.NotNull(body.Data);
        Assert.Equal(32, body.Data!.ChallengeId.Length);
        Assert.True(body.Data.ChallengeId.All(Uri.IsHexDigit));
        Assert.False(string.IsNullOrEmpty(body.Data.Nonce));
    }

    [Fact]
    public async Task Challenge_cho_thiet_bi_chua_dang_ky_van_tra_200()
    {
        var anonymousClient = NewAnonymousClient();

        var response = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge",
            new { userCode = "UC-KHONG-TON-TAI", deviceId = "device-la" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Chu_ky_sai_key_thi_verify_that_bai()
    {
        var (publicKey, _) = NewKeyPair();
        var (_, wrongKey) = NewKeyPair();
        var (_, userCode, deviceId) = await RegisterDeviceAsync(publicKey);
        var anonymousClient = NewAnonymousClient();

        var challengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>())!.Data!;

        var signature = Sign(wrongKey, challenge.Nonce);
        var verifyResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = challenge.ChallengeId, signature });

        var body = await verifyResponse.Content.ReadFromJsonAsync<BiometricFailureBody>();
        AssertGenericBiometricFailure(verifyResponse.StatusCode, body);
    }

    [Fact]
    public async Task Dung_lai_cung_challengeId_lan_thu_hai_thi_that_bai()
    {
        var (publicKey, key) = NewKeyPair();
        var (_, userCode, deviceId) = await RegisterDeviceAsync(publicKey);
        var anonymousClient = NewAnonymousClient();

        var challengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>())!.Data!;
        var signature = Sign(key, challenge.Nonce);

        var first = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify", new { challengeId = challenge.ChallengeId, signature });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify", new { challengeId = challenge.ChallengeId, signature });
        var secondBody = await second.Content.ReadFromJsonAsync<BiometricFailureBody>();
        AssertGenericBiometricFailure(second.StatusCode, secondBody);
    }

    /// <summary>
    /// P5/P12: hai request verify chay THUC SU song song tren cung mot challengeId — dung
    /// mot lan phai co hieu luc ngay ca duoi race, khong chi khi goi tuan tu.
    /// </summary>
    [Fact]
    public async Task Hai_verify_song_song_cung_challengeId_chi_dung_mot_lan_thanh_cong()
    {
        var (publicKey, key) = NewKeyPair();
        var (_, userCode, deviceId) = await RegisterDeviceAsync(publicKey);
        var anonymousClient = NewAnonymousClient();

        var challengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>())!.Data!;
        var signature = Sign(key, challenge.Nonce);

        var t1 = anonymousClient.PostAsJsonAsync("/auth/biometric/verify", new { challengeId = challenge.ChallengeId, signature });
        var t2 = anonymousClient.PostAsJsonAsync("/auth/biometric/verify", new { challengeId = challenge.ChallengeId, signature });
        var results = await Task.WhenAll(t1, t2);

        var okCount = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var failCount = results.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);
        Assert.Equal(1, okCount);
        Assert.Equal(1, failCount);
    }

    // Finding 3 (final review, hardening) da duoc fix truc tiep trong BiometricAuthService.VerifyAsync
    // (doc lai RevokedAt tu DB NGAY SAU khi phat token, huy token vua phat neu credential vua bi
    // revoke boi mot request khac trong luc do). DA THU nhung KHONG viet duoc mot test tu dong
    // dang tin cay cho rieng race nay — xem final-fix-report.md, muc Finding 3, ly do ky thuat
    // chi tiet (MemoryCacheRedisService trong moi truong test khong co I/O that, nen toan bo
    // khoang ho giua "phat token" va "doc lai RevokedAt" hoan tat trong cung mot lan chay dong
    // bo — mot updater ngoai (that su phai qua vong Postgres/scheduler rieng) khong bao giờ kip
    // xen vao giua, da thu 3 lan voi ca Task.Delay lan vong lap Task.Yield 200k lan deu thua).

    [Fact]
    public async Task Thu_hoi_credential_roi_verify_thi_that_bai()
    {
        var (publicKey, key) = NewKeyPair();
        var (testUser, userCode, deviceId) = await RegisterDeviceAsync(publicKey);
        var anonymousClient = NewAnonymousClient();

        var revokeResponse = await testUser.Client.DeleteAsync($"/auth/biometric/{deviceId}");
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        var challengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>())!.Data!;
        var signature = Sign(key, challenge.Nonce);

        var verifyResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = challenge.ChallengeId, signature });

        var body = await verifyResponse.Content.ReadFromJsonAsync<BiometricFailureBody>();
        AssertGenericBiometricFailure(verifyResponse.StatusCode, body);
    }

    /// <summary>
    /// Fix round 1 (SECURITY): revoke credential khong duoc chi "tat loi tat sinh trac" — no
    /// phai khoa han thiet bi do. Kich ban thuc te: dien thoai bi mat cap van con giu mot phien
    /// (access+refresh) con song tu truoc do; neu revoke chi doi RevokedAt ma khong dung phien
    /// do, ke giu may van dung phien cu duoc tiep, va tham chi con qua duoc P6 (deviceId khop
    /// phien) de dang ky lai key cua chinh minh — "revoke" tro thanh vo nghia.
    ///
    /// Finding 2 (final review) doi lai: TU revoke tren CHINH thiet bi dang dung khong con giet
    /// phien hien tai nua (xem test Revoke_tu_chinh_thiet_bi_dang_dung_khong_tu_dang_xuat_phien_hien_tai
    /// ben duoi) — nen test nay duoc doi thanh kich ban REVOKE TU XA (goi tu mot phien tren
    /// THIET BI KHAC cua CUNG user) de van con kiem tra dung dieu Fix round 1 dat ra: revoke tu
    /// xa van phai giet ca access token lan refresh token cua thiet bi bi revoke.
    /// </summary>
    [Fact]
    public async Task Thu_hoi_tu_xa_thu_hoi_ca_access_va_refresh_token_cua_thiet_bi_bi_revoke()
    {
        var (publicKey, key) = NewKeyPair();
        var (_, userCode, deviceId) = await RegisterDeviceAsync(publicKey);
        var anonymousClient = NewAnonymousClient();

        var challengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>())!.Data!;
        var signature = Sign(key, challenge.Nonce);

        var verifyResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = challenge.ChallengeId, signature });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var tokens = (await verifyResponse.Content.ReadFromJsonAsync<OperationResultOf<TokenResponse>>())!.Data!;

        // Phien vua dang nhap qua sinh trac (P10 da thu hoi phien dang ky ban dau cua testUser
        // ngay trong VerifyAsync — day moi la phien "dang song" that su cua thiet bi luc nay).
        using var sessionClient = _fixture.CreateClient();
        sessionClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        // Goi revoke tu MOT PHIEN KHAC cua cung user, tren mot thiet bi KHAC voi deviceId dang
        // bi revoke — day la revoke TU XA (finding 2 khong ap dung, callerDeviceId != deviceId).
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = await db.Users.SingleAsync(u => u.UserCode == userCode);
        var accessTokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var otherDeviceAccessToken = await accessTokens.CreateForCustomerAsync(
            user, new DeviceInfo($"other-device-{Guid.NewGuid():N}", "Thiet bi khac", "ios"));
        using var otherDeviceClient = _fixture.CreateClient();
        otherDeviceClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", otherDeviceAccessToken);

        var revokeResponse = await otherDeviceClient.DeleteAsync($"/auth/biometric/{deviceId}");
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        // Access token cua phien bi revoke tu xa phai chet ngay.
        var meResponse = await sessionClient.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);

        // Refresh token cua CUNG thiet bi cung phai chet — khong con doi duoc cap token moi.
        var refreshResponse = await anonymousClient.PostAsJsonAsync("/auth/refresh-token",
            new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    /// <summary>
    /// Finding 2 (final review): tu tat sinh trac NGAY TREN thiet bi dang dung (vd nguoi dung
    /// mo cai dat tren chinh dien thoai va tat cong tac "dang nhap sinh trac") KHONG duoc tu
    /// dang xuat ho khoi phien ho dang dung — day la thao tac UI chinh theo spec muc 5.5, khac
    /// voi kich ban "mat may/bi chiem token" (revoke tu xa) o test tren.
    /// </summary>
    [Fact]
    public async Task Revoke_tu_chinh_thiet_bi_dang_dung_khong_tu_dang_xuat_phien_hien_tai()
    {
        var (publicKey, key) = NewKeyPair();
        var (testUser, userCode, deviceId) = await RegisterDeviceAsync(publicKey);

        // testUser.Client dang mang chinh phien cua deviceId nay (P6 doi hoi register tren
        // dung phien) — goi revoke tu CHINH client nay la kich ban tu-tat-sinh-trac.
        var revokeResponse = await testUser.Client.DeleteAsync($"/auth/biometric/{deviceId}");
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        // Phien hien tai KHONG duoc chet.
        var meResponse = await testUser.Client.GetAsync("/account/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        // Nhung credential van phai bi revoke that su — verify sau do van phai that bai.
        var anonymousClient = NewAnonymousClient();
        var challengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>())!.Data!;
        var signature = Sign(key, challenge.Nonce);

        var verifyResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = challenge.ChallengeId, signature });
        var body = await verifyResponse.Content.ReadFromJsonAsync<BiometricFailureBody>();
        AssertGenericBiometricFailure(verifyResponse.StatusCode, body);
    }

    /// <summary>P12: dang ky lai cung (user, device) phai THANH CONG va ghi de key cu — key cu
    /// khong con dang nhap duoc, chi key moi dung.</summary>
    [Fact]
    public async Task Dang_ky_lai_cung_thiet_bi_ghi_de_key_cu_thanh_cong()
    {
        var (oldPublicKey, oldKey) = NewKeyPair();
        var (testUser, userCode, deviceId) = await RegisterDeviceAsync(oldPublicKey);

        var (newPublicKey, newKey) = NewKeyPair();
        var reRegisterResponse = await testUser.Client.PostAsJsonAsync("/auth/biometric/register", new
        {
            deviceId,
            deviceName = "Test Phone",
            platform = "android",
            publicKey = newPublicKey
        });
        Assert.Equal(HttpStatusCode.OK, reRegisterResponse.StatusCode);

        var anonymousClient = NewAnonymousClient();

        // Key cu khong con dung duoc.
        var oldChallengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        var oldChallenge = (await oldChallengeResponse.Content.ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>())!.Data!;
        var oldSignature = Sign(oldKey, oldChallenge.Nonce);
        var oldVerifyResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = oldChallenge.ChallengeId, signature = oldSignature });
        Assert.Equal(HttpStatusCode.Unauthorized, oldVerifyResponse.StatusCode);

        // Key moi dang nhap duoc.
        var newChallengeResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/challenge", new { userCode, deviceId });
        var newChallenge = (await newChallengeResponse.Content.ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>())!.Data!;
        var newSignature = Sign(newKey, newChallenge.Nonce);
        var newVerifyResponse = await anonymousClient.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = newChallenge.ChallengeId, signature = newSignature });
        Assert.Equal(HttpStatusCode.OK, newVerifyResponse.StatusCode);
    }

    /// <summary>P6 (SECURITY): khong duoc dang ky sinh trac cho mot deviceId khac voi deviceId
    /// cua chinh phien access token dang dung — chan "token ngan han bi lo => dang nhap sinh
    /// trac vinh vien cho thiet bi bat ky".</summary>
    [Fact]
    public async Task Dang_ky_cho_deviceId_khac_voi_phien_hien_tai_bi_tu_choi()
    {
        var (publicKey, _) = NewKeyPair();
        var sessionDeviceId = $"device-{Guid.NewGuid():N}";
        var testUser = await TestUser.CreateAsync(_fixture, new DeviceInfo(sessionDeviceId, "Test Phone", "android"));

        var otherDeviceId = $"device-{Guid.NewGuid():N}";
        var response = await testUser.Client.PostAsJsonAsync("/auth/biometric/register", new
        {
            deviceId = otherDeviceId,
            deviceName = "Thiet bi khac",
            platform = "android",
            publicKey
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// P12/P16: moi nhanh that bai khac nhau cua verify (challenge khong ton tai, sai user,
    /// sai thiet bi, khong co credential, chu ky sai) deu phai tra ve CUNG mot ma loi/thong
    /// diep — client khong duoc phan biet duoc nguyen nhan that.
    /// </summary>
    [Fact]
    public async Task Moi_nhanh_that_bai_cua_verify_tra_ve_cung_mot_loi_generic()
    {
        var (publicKey, key) = NewKeyPair();
        var (_, userCode, deviceId) = await RegisterDeviceAsync(publicKey);
        var (_, wrongKey) = NewKeyPair();

        var responses = new List<(string Scenario, HttpResponseMessage Response)>();
        var client = NewAnonymousClient();

        // 1) ChallengeId khong ton tai (chua tung phat hanh / da het han).
        var missingSignature = Sign(key, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        responses.Add(("missing_challenge", await client.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = Guid.NewGuid().ToString("N"), signature = missingSignature })));

        // 2) UserCode khong ton tai trong he thong.
        var noUserChallenge = (await (await client.PostAsJsonAsync("/auth/biometric/challenge",
            new { userCode = "UC-KHONG-TON-TAI", deviceId })).Content.ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>())!.Data!;
        responses.Add(("wrong_user", await client.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = noUserChallenge.ChallengeId, signature = Sign(key, noUserChallenge.Nonce) })));

        // 3) User co that nhung deviceId chua tung dang ky sinh trac.
        var wrongDeviceChallenge = (await (await client.PostAsJsonAsync("/auth/biometric/challenge",
            new { userCode, deviceId = $"device-chua-dang-ky-{Guid.NewGuid():N}" })).Content
            .ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>())!.Data!;
        responses.Add(("no_credential", await client.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = wrongDeviceChallenge.ChallengeId, signature = Sign(key, wrongDeviceChallenge.Nonce) })));

        // 4) Chu ky sai (key dung nhung khong khop credential da dang ky).
        var badSigChallenge = (await (await client.PostAsJsonAsync("/auth/biometric/challenge",
            new { userCode, deviceId })).Content.ReadFromJsonAsync<OperationResultOf<BiometricChallengeResponse>>())!.Data!;
        responses.Add(("bad_signature", await client.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = badSigChallenge.ChallengeId, signature = Sign(wrongKey, badSigChallenge.Nonce) })));

        // 5) Challenge da dung roi (replay) — dung lai chinh challenge #4 sau khi no da bi
        // tieu thu boi lan verify that bai o tren (VerifyAsync xoa key truoc khi kiem tra chu ky).
        responses.Add(("replayed_challenge", await client.PostAsJsonAsync("/auth/biometric/verify",
            new { challengeId = badSigChallenge.ChallengeId, signature = Sign(key, badSigChallenge.Nonce) })));

        string? expectedErrorCode = null;
        string? expectedMessage = null;

        foreach (var (scenario, response) in responses)
        {
            var body = await response.Content.ReadFromJsonAsync<BiometricFailureBody>();
            Assert.True(HttpStatusCode.Unauthorized == response.StatusCode, $"scenario={scenario} status={response.StatusCode}");
            Assert.NotNull(body);
            Assert.False(body!.Success, $"scenario={scenario}");
            Assert.NotNull(body.ErrorCode);

            expectedErrorCode ??= body.ErrorCode;
            Assert.Equal(expectedErrorCode, body.ErrorCode);

            expectedMessage ??= body.Message;
            Assert.Equal(expectedMessage, body.Message);
        }
    }
}

/// <summary>Doc Message cung luc voi Success/ErrorCode trong mot lan doc content duy nhat —
/// OperationResultOf&lt;T&gt; (dung chung voi SessionManagementTests.cs) khong co truong nay,
/// va doc HttpContent hai lan cho hai kieu khac nhau se that bai (stream da doc het).</summary>
public class BiometricFailureBody
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}
