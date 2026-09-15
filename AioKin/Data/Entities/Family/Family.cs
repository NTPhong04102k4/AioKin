using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AioKin.Data.Entities.Family;

/// <summary>
/// Mot ho gia dinh — pham vi chia se cho chat nhom, kho file, so chi tieu va lich chung.
///
/// Khong co cot InviteCode co dinh o day, du ban thiet ke goc co nhac: mot ma vinh vien
/// khong het han va khong gioi han so lan dung se nam lai trong mot anh chup man hinh nao do
/// mai mai. Ma moi song trong <see cref="FamilyInvite"/>, co han su dung va thu hoi duoc.
/// </summary>
[Table("families", Schema = "family")]
public class Family
{
    /// <summary>Chuoi <c>subject</c> trong rule phan quyen. Phai khop hang so cung ten ben app.</summary>
    public const string SubjectType = "Family";

    [Key]
    public Guid FamilyID { get; set; } = Guid.NewGuid();

    /// <summary>Id cong khai — moi route va DTO dung no. Khong lo FamilyID noi bo.</summary>
    public Guid FamilyUUID { get; set; } = Guid.NewGuid();

    [MaxLength(120)]
    public required string Name { get; set; }

    /// <summary>
    /// Chu ho hien tai, tro toi <c>users.user_id</c> noi bo.
    ///
    /// KHONG phai mot rang buoc duy nhat: mot gia dinh co the co nhieu thanh vien mang vai
    /// tro <see cref="FamilyMemberRole.Owner"/>. Bat bien that su la "luon con it nhat mot
    /// thanh vien Owner dang hoat dong", va no duoc giu bang cach DEM so dong Owner chu
    /// khong bang cach so sanh voi cot nay.
    /// </summary>
    public Guid OwnerUserID { get; set; }

    /// <summary>Han muc dung luong kho file, don vi byte. M0 chi luu; M3 moi cuong che.</summary>
    public long StorageQuotaBytes { get; set; } = DefaultQuotaBytes;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public ICollection<FamilyMember> Members { get; set; } = [];

    /// <summary>10 GB — mac dinh o muc 5.5 cua ban ke hoach.</summary>
    public const long DefaultQuotaBytes = 10L * 1024 * 1024 * 1024;
}
