using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Family;

/// <summary>
/// Ma moi vao gia dinh. Ba gioi han bat buoc, moi cai chan mot duong lam dung khac nhau:
/// het han theo thoi gian, gioi han so lan dung, va thu hoi duoc bang tay.
/// </summary>
[Table("family_invites", Schema = "family")]
public class FamilyInvite
{
    [Key]
    public Guid FamilyInviteID { get; set; } = Guid.NewGuid();

    public Guid FamilyID { get; set; }

    [ForeignKey(nameof(FamilyID))]
    public Family? Family { get; set; }

    /// <summary>Duy nhat tren toan he thong — tra cuu chi bang ma, khong kem gia dinh nao.</summary>
    [MaxLength(16)]
    public required string Code { get; set; }

    public Guid CreatedByUserID { get; set; }

    public DateTime ExpiresAt { get; set; }

    public int MaxUses { get; set; }

    public int UsedCount { get; set; }

    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>Con dung duoc khong. Kiem tra ca ba gioi han o mot cho.</summary>
    public bool IsUsable(DateTime nowUtc)
        => RevokedAt is null && ExpiresAt > nowUtc && UsedCount < MaxUses;
}
