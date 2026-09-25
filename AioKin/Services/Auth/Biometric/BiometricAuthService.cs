using System.Security.Cryptography;
using AioKin.Common;
using AioKin.Data;
using AioKin.Data.Entities.Security;
using AioKin.Models.InputModel.Auth.Biometric;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.ViewModel.Auth.Biometric;
using AioKin.Models.ViewModel.Auth.User;
using AioKin.Services.Auth.RefreshToken;
using AioKin.Services.Auth.Token;
using AioKin.Services.Auth.User;
using AioKin.Services.Common.Cache;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Auth.Biometric;

public class BiometricAuthService : IBiometricAuthService
{
    private readonly AioKinDbContext _db;
    private readonly IUserService _userService;
    private readonly IRedisService _redis;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ILogger<BiometricAuthService> _logger;

    public BiometricAuthService(
        AioKinDbContext db,
        IUserService userService,
        IRedisService redis,
        IAccessTokenService accessTokenService,
        IRefreshTokenService refreshTokenService,
        ILogger<BiometricAuthService> logger)
    {
        _db = db;
        _userService = userService;
        _redis = redis;
        _accessTokenService = accessTokenService;
        _refreshTokenService = refreshTokenService;
        _logger = logger;
    }

    public async Task<OperationResult> RegisterAsync(Guid userUuid, RegisterBiometricRequest request)
    {
        var user = await _userService.GetByUuidAsync(userUuid);
        if (user is null)
            return OperationResult.Fail("NotFound", "Khong tim thay nguoi dung.");

        // P7: xac nhan day la mot public key P-256 that truoc khi luu — key sai dinh dang/sai
        // duong cong khong duoc chap nhan tu luc dang ky, khong doi den luc verify moi phat hien.
        if (!TryImportP256PublicKey(request.PublicKey, out var invalidKeyReason))
            return OperationResult.Fail("InvalidPublicKey", invalidKeyReason);

        var existing = await _db.DeviceCredentials
            .FirstOrDefaultAsync(c => c.UserID == user.UserID && c.DeviceId == request.DeviceId);

        if (existing is not null)
        {
            // Doi key (cai lai app, xoay key) — ghi de, khong tao dong moi, va mo lai neu
            // truoc do da bi revoke.
            existing.PublicKey = request.PublicKey;
            existing.DeviceName = request.DeviceName;
            existing.Platform = request.Platform;
            existing.RevokedAt = null;
        }
        else
        {
            _db.DeviceCredentials.Add(new DeviceCredential
            {
                UserID = user.UserID,
                DeviceId = request.DeviceId,
                DeviceName = request.DeviceName,
                Platform = request.Platform,
                PublicKey = request.PublicKey
            });
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Biometric credential registered for userCode={UserCode}, deviceId={DeviceId}", user.UserCode, request.DeviceId);
        return OperationResult.Ok("Da dang ky dang nhap sinh trac cho thiet bi nay.");
    }

    public async Task<OperationResult> ChallengeAsync(BiometricChallengeRequest request)
    {
        var challengeId = Guid.NewGuid().ToString("N");
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        // Luon luu va tra ve, KHONG kiem tra credential co ton tai o day: kiem tra som se
        // lo (userCode, deviceId) nao dang co dang ky sinh trac qua response 200/khac.
        var saved = await _redis.SetAsync(
            RedisKeys.BiometricChallenge(challengeId),
            new BiometricChallenge(request.UserCode, request.DeviceId, nonce),
            RedisTtl.BiometricChallenge);

        if (!saved)
        {
            // P22: khong duoc tra ve nonce "thanh cong" ma phia sau khong con gi trong Redis
            // de VerifyAsync tim thay — fail closed, giong AccessTokenService.IssueAsync.
            _logger.LogError("Failed to persist biometric challenge to Redis for deviceId={DeviceId}", request.DeviceId);
            return OperationResult.Fail("InternalError", "Khong tao duoc challenge. Vui long thu lai.");
        }

        return OperationResult.Ok(data: new BiometricChallengeResponse { ChallengeId = challengeId, Nonce = nonce });
    }

    public async Task<OperationResult> VerifyAsync(BiometricVerifyRequest request)
    {
        var key = RedisKeys.BiometricChallenge(request.ChallengeId);
        var challenge = await _redis.GetAsync<BiometricChallenge>(key);

        // P5: xoa NGAY sau khi doc, va CHI tiep tuc neu THUC SU la request nay da xoa duoc
        // key (DeleteAsync tra true chi khi con ton tai va vua bi xoa). Day la thu lam cho
        // challenge dung mot lan mot cach nguyen tu o muc tung thao tac: neu hai request verify
        // chay song song tren cung mot challengeId, ca hai deu doc duoc challenge, nhung chi
        // DUY NHAT mot cuoc goi DeleteAsync tra ve true — cuoc goi con lai phai fail ngay ca
        // khi no co chu ky dung, dong cua so replay song song.
        var deleted = await _redis.DeleteAsync(key);

        if (challenge is null || !deleted)
            return Fail(null, "no_challenge");

        var user = await _userService.GetByUserCodeAsync(challenge.UserCode);
        if (user is null || !user.IsActive || user.IsLocked)
            return Fail(challenge.DeviceId, "no_user");

        var credential = await _db.DeviceCredentials
            .FirstOrDefaultAsync(c => c.UserID == user.UserID && c.DeviceId == challenge.DeviceId && c.RevokedAt == null);
        if (credential is null)
            return Fail(challenge.DeviceId, "no_credential");

        if (!BiometricSignature.Verify(credential.PublicKey, challenge.Nonce, request.Signature))
            return Fail(challenge.DeviceId, "bad_signature");

        credential.LastUsedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var device = new DeviceInfo(challenge.DeviceId, credential.DeviceName, credential.Platform);

        // P10: thu hoi access session + refresh token con song cua CUNG thiet bi truoc khi
        // phat cap moi — tranh 2 phien song song cho cung mot thiet bi (giong duong RefreshToken
        // da lam, dong G7 cho ca duong sinh trac).
        await _accessTokenService.RevokeForDeviceAsync(user.UserCode, challenge.DeviceId);
        await _refreshTokenService.RevokeAllForDeviceAsync(user.UserCode, challenge.DeviceId);

        var accessToken = await _accessTokenService.CreateForCustomerAsync(user, device);
        var refreshToken = await _refreshTokenService.GenerateAsync(user.UserCode, Roles.CUSTOMER, device);

        // Finding 3 (hardening): dong hep race — giua luc credential duoc doc o dau ham va luc
        // token vua duoc phat xong o tren, mot RevokeAllForUserAsync khac (vd doi mat khau/dat
        // lai mat khau xay ra CUNG luc o request khac) co the da danh dau CHINH credential nay
        // la RevokedAt. Doc lai RevokedAt truc tiep tu DB (khong dung lai bien credential da
        // doc dau ham) ngay sau khi phat token — neu da bi revoke trong luc do, huy ngay cap
        // token vua phat va tra ve cung loi generic thay vi tra token cho mot credential vua bi
        // thu hoi.
        var revokedDuringIssue = await _db.DeviceCredentials
            .Where(c => c.DeviceCredentialID == credential.DeviceCredentialID)
            .Select(c => c.RevokedAt)
            .FirstOrDefaultAsync();

        if (revokedDuringIssue is not null)
        {
            await _accessTokenService.RevokeForDeviceAsync(user.UserCode, challenge.DeviceId);
            await _refreshTokenService.RevokeAllForDeviceAsync(user.UserCode, challenge.DeviceId);
            return Fail(challenge.DeviceId, "revoked_race");
        }

        _logger.LogInformation("Biometric login succeeded for userCode={UserCode}, deviceId={DeviceId}", user.UserCode, challenge.DeviceId);

        return OperationResult.Ok("Dang nhap thanh cong.", new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _accessTokenService.AccessTokenLifetimeSeconds,
            TokenType = "Bearer",
            Scope = Roles.CUSTOMER
        });

        // Mot diem loi duy nhat cho moi truong hop that bai (P16) — khong lo buoc nao trong so
        // "challenge het han/da dung", "khong co user", "khong co credential", "chu ky sai" la
        // nguyen nhan that qua response. P18: van ghi Warning noi bo (deviceId + loai loi ngan
        // gon) de con dieu tra/chinh rate-limit sau nay — khong bao gio log key/nonce/chu ky.
        OperationResult Fail(string? deviceId, string reason)
        {
            _logger.LogWarning("Biometric verify failed, deviceId={DeviceId}, reason={Reason}", deviceId ?? "unknown", reason);
            return OperationResult.Fail("InvalidCredentials", "Khong dang nhap duoc bang sinh trac hoc.");
        }
    }

    public async Task<OperationResult> RevokeAsync(Guid userUuid, string deviceId, string? callerDeviceId = null)
    {
        var user = await _userService.GetByUuidAsync(userUuid);
        if (user is null)
            return OperationResult.Fail("NotFound", "Khong tim thay nguoi dung.");

        return await RevokeCoreAsync(user, deviceId, callerDeviceId);
    }

    public async Task<OperationResult> RevokeAsync(string userCode, string deviceId)
    {
        var user = await _userService.GetByUserCodeAsync(userCode);
        if (user is null)
            return OperationResult.Fail("NotFound", "Khong tim thay nguoi dung.");

        // AccountController.DeleteSession (finding 1) luon la revoke tu xa cho 1 session cu
        // the — khong phai tu tat sinh trac tren chinh thiet bi dang dung, nen callerDeviceId
        // luon null va phien/refresh token cua thiet bi do luon bi thu hoi (khong bo qua).
        return await RevokeCoreAsync(user, deviceId, callerDeviceId: null);
    }

    /// <summary>
    /// Loi chung cho hai overload RevokeAsync o tren: tim dung credential con hieu luc cua
    /// dung user + deviceId, danh dau RevokedAt, roi thu hoi phien/refresh token cua thiet bi
    /// do TRU KHI callerDeviceId trung voi deviceId dang bi revoke.
    /// </summary>
    private async Task<OperationResult> RevokeCoreAsync(AioKin.Data.Entities.Security.User user, string deviceId, string? callerDeviceId)
    {
        var credential = await _db.DeviceCredentials
            .FirstOrDefaultAsync(c => c.UserID == user.UserID && c.DeviceId == deviceId && c.RevokedAt == null);

        if (credential is null)
            return OperationResult.Fail("NotFound", "Khong tim thay dang ky sinh trac cho thiet bi nay.");

        RevokeCredential(credential);
        await _db.SaveChangesAsync();

        // Fix round 1 (SECURITY): revoke credential thoi la chua du — neu thiet bi (vd dien
        // thoai bi mat cap) van con mot access/refresh token con song tu truoc, ke dang giu
        // no van qua duoc P6 (deviceId khop phien) va dang ky lai duoc key cua chinh minh, phuc
        // hoi dang nhap sinh trac "vinh vien" ngay sau khi chu that su vua tat no di. Dung lai
        // cung primitive VerifyAsync da dung o P10 — thu hoi ca access session lan refresh
        // token con song cua DUNG thiet bi nay, de "revoke" nghia la khoa han thiet bi do, khong
        // chi la tat loi tat sinh trac.
        //
        // Finding 2: NGOAI LE cho truong hop tu tat sinh trac NGAY TREN thiet bi dang dung
        // (callerDeviceId == deviceId) — day la thao tac UI chinh theo spec muc 5.5, khong
        // phai kich ban "mat may/bi chiem token", nen KHONG duoc tu dang xuat nguoi dung khoi
        // chinh phien ho dang dung chi vi ho tat mot cong tac cai dat.
        if (!string.Equals(callerDeviceId, deviceId, StringComparison.Ordinal))
            await RevokeDeviceSessionsAsync(user.UserCode, deviceId);

        _logger.LogInformation("Biometric credential revoked for userCode={UserCode}, deviceId={DeviceId}", user.UserCode, deviceId);
        return OperationResult.Ok("Da tat dang nhap sinh trac cho thiet bi nay.");
    }

    /// <summary>
    /// Task 5 (P20): doi mat khau/dat lai mat khau thu hoi TOAN BO credential sinh trac con
    /// hieu luc cua user, khong chi mot thiet bi — mat khau bi lo thi moi thiet bi da dang ky
    /// sinh trac deu phai dang ky lai tu dau. Danh dau RevokedAt cho tat ca truoc (mot lan
    /// SaveChangesAsync), roi moi thu hoi phien song cua tung thiet bi — tranh N lan ghi DB
    /// rieng le khi user co nhieu thiet bi.
    /// </summary>
    public async Task RevokeAllForUserAsync(string userCode)
    {
        var user = await _userService.GetByUserCodeAsync(userCode);
        if (user is null)
            return;

        var credentials = await _db.DeviceCredentials
            .Where(c => c.UserID == user.UserID && c.RevokedAt == null)
            .ToListAsync();

        // Khong co credential nao dang hieu luc — khong lam gi, va TUYET DOI khong nem loi:
        // doi/dat lai mat khau van phai thanh cong binh thuong cho user chua tung dung sinh trac.
        if (credentials.Count == 0)
            return;

        foreach (var credential in credentials)
            RevokeCredential(credential);

        await _db.SaveChangesAsync();

        foreach (var credential in credentials)
            await RevokeDeviceSessionsAsync(user.UserCode, credential.DeviceId);

        _logger.LogInformation(
            "All biometric credentials revoked for userCode={UserCode}, count={Count}", user.UserCode, credentials.Count);
    }

    /// <summary>
    /// Primitive dung chung cho moi duong revoke tung dong credential — RevokeAsync dung truc
    /// tiep, va RevokeAllForUserAsync (Task 5) lap qua danh sach credential cua user roi goi
    /// lai chinh ham nay cho tung dong, khong viet lai logic revoke.
    /// </summary>
    private static void RevokeCredential(DeviceCredential credential)
        => credential.RevokedAt = DateTime.UtcNow;

    /// <summary>
    /// Thu hoi access session + refresh token con song cua mot thiet bi — dung chung giua
    /// RevokeAsync (1 thiet bi) va RevokeAllForUserAsync (Task 5, moi thiet bi cua user), tranh
    /// lap lai cung 2 dong goi 2 service.
    /// </summary>
    private async Task RevokeDeviceSessionsAsync(string userCode, string deviceId)
    {
        await _accessTokenService.RevokeForDeviceAsync(userCode, deviceId);
        await _refreshTokenService.RevokeAllForDeviceAsync(userCode, deviceId);
    }

    /// <summary>
    /// P7: import SubjectPublicKeyInfo tu base64 va xac nhan la P-256 that, tai su dung logic
    /// OID/ten duong cong cua BiometricSignature (internal static) thay vi lam lai. Tra ve
    /// thong diep loi ro rang — day la nhanh dang ky (khong anonymous), khac VerifyAsync
    /// (P16) noi moi that bai phai giong het nhau.
    /// </summary>
    private static bool TryImportP256PublicKey(string publicKeyBase64, out string reason)
    {
        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(publicKeyBase64);
        }
        catch (FormatException)
        {
            reason = "Public key khong dung dinh dang base64.";
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(keyBytes, out _);

            if (!BiometricSignature.IsValidP256PublicKey(ecdsa))
            {
                reason = "Public key phai la duong cong P-256 (SubjectPublicKeyInfo).";
                return false;
            }
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            reason = "Public key khong hop le.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
