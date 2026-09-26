using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Data.Entities.Vault;

/// <summary>
/// Pham vi chia se cho Prompt/Category/Tag. KHONG phai nguon su that ve thanh vien cho
/// SpaceType.Family — nguon that van la family.family_members, xem SpaceContext.
/// </summary>
[Table("spaces", Schema = "promptvault")]
public class Space
{
    public const string SubjectType = "Space";

    [Key]
    public Guid SpaceID { get; set; } = Guid.NewGuid();

    /// <summary>Id cong khai — moi route/DTO dung no, khong lo SpaceID noi bo.</summary>
    public Guid SpaceUUID { get; set; } = Guid.NewGuid();

    public SpaceType SpaceType { get; set; }

    [MaxLength(120)]
    public required string Name { get; set; }

    public Guid OwnerUserID { get; set; }

    /// <summary>Chi co gia tri khi SpaceType = Family. 1 family = 1 space (unique).</summary>
    public Guid? FamilyID { get; set; }

    [ForeignKey(nameof(FamilyID))]
    public FamilyDb? Family { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
