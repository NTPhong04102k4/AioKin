using AioKin.Models.InputModel.Auth.Biometric;
using AioKin.Models.InputModel.Auth.User;

namespace AioKin.Services.Auth.Biometric;

/// <summary>
/// Dang nhap bang sinh trac hoc: dang ky public key cua thiet bi, challenge/response mot lan,
/// va thu hoi. Server khong bao gio nhan/luu du lieu sinh trac — chi verify chu ky ECDSA cua
/// mot nonce dung mot lan.
/// </summary>
public interface IBiometricAuthService
{
    /// <summary>
    /// Dang ky/cap nhat public key cua mot thiet bi cho user dang dang nhap.
    /// <paramref name="userUuid"/> la UserUuid cong khai (tu claim token) — RegisterAsync tu
    /// tra ra UserID noi bo. Controller phai doi chieu request.DeviceId voi DeviceId cua chinh
    /// phien dang dung (P6) TRUOC khi goi ham nay.
    /// </summary>
    Task<OperationResult> RegisterAsync(Guid userUuid, RegisterBiometricRequest request);

    /// <summary>
    /// Luon tra ve mot challenge moi (Data la BiometricChallengeResponse khi Success), bat ke
    /// (userCode, deviceId) co dang ky hay chua — khong duoc lo thong tin nay qua response.
    /// That bai (OperationResult.Fail) chi khi ha tang (Redis) that su khong luu duoc.
    /// </summary>
    Task<OperationResult> ChallengeAsync(BiometricChallengeRequest request);

    /// <summary>
    /// Xac thuc chu ky va phat token neu hop le. Data la TokenResponse khi Success. Moi nhanh
    /// that bai (challenge het han/da dung, sai user, sai thiet bi, khong co credential, sai
    /// chu ky) deu tra ve CUNG mot OperationResult.Fail generic (P16) — khong duoc phan biet
    /// qua response.
    /// </summary>
    Task<OperationResult> VerifyAsync(BiometricVerifyRequest request);

    /// <summary>
    /// Thu hoi (revoked_at) credential cua chinh user cho 1 deviceId. Dung chung mot buoc
    /// "danh dau RevokedAt + luu" voi con duong nay se duoc RevokeAllForUserAsync (Task 5,
    /// doi mat khau/reset mat khau) tai su dung sau nay — khong lam rieng logic revoke o day.
    /// </summary>
    Task<OperationResult> RevokeAsync(Guid userUuid, string deviceId);

    /// <summary>
    /// Task 5 (P20): doi mat khau/dat lai mat khau phai thu hoi TOAN BO credential sinh trac
    /// con hieu luc cua user nay — khong chi mot thiet bi. Dung userCode (khong phai uuid) vi
    /// AuthController.ResetPassword/AccountController.ChangePassword da co san User tu luc
    /// xac thuc mat khau, khong can tra lai qua GetByUuidAsync. Khong co credential nao thi
    /// khong lam gi (khong nem loi) — doi mat khau van phai thanh cong binh thuong.
    /// </summary>
    Task RevokeAllForUserAsync(string userCode);
}
