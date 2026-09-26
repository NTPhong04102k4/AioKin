using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Security;

/// <summary>
/// FCM Registration Token cua mot thiet bi nguoi dung, dung de gui push notification
/// (silent data sync wakeup, AI prompt enrichment xong, app update broadcast).
/// </summary>
[Table("device_tokens", Schema = "security")]
public class DeviceToken
{
    [Key]
    public Guid DeviceTokenID { get; set; } = Guid.NewGuid();

    /// <summary>Khoa ngoai toi security.users — token thuoc so huu cua mot user.</summary>
    public Guid UserID { get; set; }

    /// <summary>FCM registration token do Google phat cho thiet bi.</summary>
    [MaxLength(512)]
    public required string Token { get; set; }

    /// <summary>Nen tang: android, ios, web.</summary>
    [MaxLength(50)]
    public string Platform { get; set; } = "android";

    /// <summary>Device identifier on dinh do client gui len (trung voi DeviceId cua session).</summary>
    [MaxLength(100)]
    public string? DeviceId { get; set; }

    /// <summary>True neu token con hieu luc. False khi logout hoac FCM bao Unregistered/dead.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }
}
