using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Sync;

/// <summary>
/// So dang ky thiet bi duy nhat. Neu 3 plan auth (opaque token/session/biometric) da chay,
/// security.device_credentials nen tro ve day thay vi tu luu ten/nen tang rieng — xem spec
/// muc 6.5. Plan nay tu dung duoc du plan auth chua chay.
///
/// Khoa chinh la composite (UserID, DeviceID), KHONG phai DeviceID rieng le (P16): neu chi
/// khoa tren DeviceID, mot user dang nhap co the gui deviceId trung voi thiet bi cua user
/// khac va ghi de dong cua ho. DeviceID dung o day luon phai lay tu phien dang nhap cua chinh
/// caller (vi du claim trong JWT), khong bao gio nhan tu body ma tin la cua nguoi khac.
/// </summary>
[Table("devices", Schema = "sync")]
public class Device
{
    public Guid UserID { get; set; }

    [MaxLength(100)]
    public required string DeviceID { get; set; }

    [MaxLength(120)]
    public string? DeviceName { get; set; }

    [MaxLength(20)]
    public string? Platform { get; set; }

    [MaxLength(20)]
    public string? AppVersion { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    public bool IsStale { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
