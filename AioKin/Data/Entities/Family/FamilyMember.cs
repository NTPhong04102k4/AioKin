using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Data.Entities.Family;

/// <summary>
/// Bang noi giua nguoi dung va gia dinh. La bang noi chu khong phai mot cot tren
/// <c>users</c> vi mot nguoi thuoc duoc nhieu gia dinh (quyet dinh 3, muc 12).
/// </summary>
[Table("family_members", Schema = "family")]
public class FamilyMember
{
    /// <summary>Chuoi <c>subject</c> trong rule phan quyen.</summary>
    public const string SubjectType = "FamilyMember";

    [Key]
    public Guid FamilyMemberID { get; set; } = Guid.NewGuid();

    public Guid FamilyID { get; set; }

    [ForeignKey(nameof(FamilyID))]
    public Family? Family { get; set; }

    public Guid UserID { get; set; }

    [ForeignKey(nameof(UserID))]
    public UserDb? User { get; set; }

    public FamilyMemberRole MemberRole { get; set; } = FamilyMemberRole.Adult;

    /// <summary>Ten hien thi trong pham vi gia dinh nay ("Bo", "Me", "Be Na"). Rong = dung ten tai khoan.</summary>
    [MaxLength(120)]
    public string? DisplayName { get; set; }

    public DateTime JoinedDate { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}
