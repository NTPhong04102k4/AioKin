using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Data.Entities.Vault;

/// <summary>Thanh vien cua 1 Team space. KHONG dung cho Family/Personal — xem SpaceContext.</summary>
[Table("space_members", Schema = "vault")]
public class SpaceMember
{
    [Key]
    public Guid SpaceMemberID { get; set; } = Guid.NewGuid();

    public Guid SpaceID { get; set; }

    [ForeignKey(nameof(SpaceID))]
    public Space? Space { get; set; }

    public Guid UserID { get; set; }

    [ForeignKey(nameof(UserID))]
    public UserDb? User { get; set; }

    public SpaceMemberRole MemberRole { get; set; } = SpaceMemberRole.Member;

    public DateTime JoinedDate { get; set; } = DateTime.UtcNow;
}
