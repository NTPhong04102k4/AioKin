using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Security;

/// <summary>
/// Public key ECDSA P-256 cua mot thiet bi, dung de dang nhap bang sinh trac hoc (Face
/// ID/van tay o tang OS, khong bao gio cham toi server). Private key tuong ung nam trong
/// Secure Enclave/Keystore cua thiet bi, khong bao gio roi khoi phan cung.
///
/// KHONG co SubjectType/CASL: bang nay khong di qua CRUD chung nao — moi thao tac chi tren
/// dong cua chinh nguoi goi, giong AccountController.
/// </summary>
[Table("device_credentials", Schema = "security")]
public class DeviceCredential
{
    [Key]
    public Guid DeviceCredentialID { get; set; } = Guid.NewGuid();

    /// <summary>Chi tro toi security.users — bio dang nhap la tinh nang cua Customer, khong phai Staff.</summary>
    public Guid UserID { get; set; }

    /// <summary>Id client tu sinh va giu on dinh — trung voi deviceId dung cho access/refresh token.</summary>
    [MaxLength(100)]
    public required string DeviceId { get; set; }

    [MaxLength(120)]
    public string? DeviceName { get; set; }

    [MaxLength(20)]
    public string? Platform { get; set; }

    /// <summary>SubjectPublicKeyInfo (SPKI), ma hoa base64.</summary>
    [MaxLength(256)]
    public required string PublicKey { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedDate { get; set; }

    /// <summary>Khac null nghia la da bi thu hoi — giu dong lai de con vet, khong xoa.</summary>
    public DateTime? RevokedAt { get; set; }
}
