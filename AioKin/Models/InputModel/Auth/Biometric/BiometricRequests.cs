using System.ComponentModel.DataAnnotations;

namespace AioKin.Models.InputModel.Auth.Biometric;

public class RegisterBiometricRequest
{
    /// <summary>Phai trung DeviceId cua chinh phien access token dang dung (P6) — controller
    /// tu doi chieu, khong tin gia tri nay tu client de quyet dinh danh tinh.</summary>
    [Required(ErrorMessage = "DeviceId la bat buoc.")]
    [MaxLength(100)]
    public string DeviceId { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? DeviceName { get; set; }

    [MaxLength(20)]
    public string? Platform { get; set; }

    /// <summary>SubjectPublicKeyInfo (SPKI) P-256, ma hoa base64. Gioi han bang dung cot
    /// DeviceCredential.PublicKey (Task 1) — khong duoc chat hon entity (P8).</summary>
    [Required(ErrorMessage = "PublicKey la bat buoc.")]
    [MaxLength(256)]
    public string PublicKey { get; set; } = string.Empty;
}

public class BiometricChallengeRequest
{
    [Required(ErrorMessage = "UserCode la bat buoc.")]
    [MaxLength(50)]
    public string UserCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "DeviceId la bat buoc.")]
    [MaxLength(100)]
    public string DeviceId { get; set; } = string.Empty;
}

public class BiometricVerifyRequest
{
    /// <summary>32 ky tu hex — Guid.NewGuid().ToString("N") o ChallengeAsync.</summary>
    [Required(ErrorMessage = "ChallengeId la bat buoc.")]
    [MaxLength(32)]
    public string ChallengeId { get; set; } = string.Empty;

    /// <summary>Chu ky DER (Rfc3279DerSequence) cua P-256, base64. DER toi da ~72 byte raw
    /// (SEQUENCE + 2 INTEGER co the co byte 0x00 dem dau) => toi da 96 ky tu base64 — 200 la
    /// du du, khong can noi rong.</summary>
    [Required(ErrorMessage = "Signature la bat buoc.")]
    [MaxLength(200)]
    public string Signature { get; set; } = string.Empty;
}
