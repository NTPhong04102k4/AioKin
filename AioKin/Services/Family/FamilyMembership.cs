using AioKin.Data.Entities.Family;

namespace AioKin.Services.Family;

/// <summary>
/// Ket qua cua mot lan kiem tra tu cach thanh vien. Mang ca id noi bo lan id cong khai vi
/// service goi sau do can id noi bo de ghi khoa ngoai, con controller can id cong khai de
/// phat ra JSON.
/// </summary>
/// <param name="FamilyID">Id noi bo cua gia dinh — dung cho khoa ngoai.</param>
/// <param name="FamilyUUID">Id cong khai cua gia dinh.</param>
/// <param name="UserID">Id noi bo cua nguoi goi.</param>
/// <param name="UserUUID">Id cong khai cua nguoi goi, lay tu token.</param>
/// <param name="Role">Vai tro trong pham vi gia dinh nay.</param>
public sealed record FamilyMembership(
    Guid FamilyID,
    Guid FamilyUUID,
    Guid UserID,
    Guid UserUUID,
    FamilyMemberRole Role)
{
    /// <summary>Owner va Adult moi tao duoc ma moi. Child thi khong.</summary>
    public bool CanInvite => Role is FamilyMemberRole.Owner or FamilyMemberRole.Adult;

    public bool IsOwner => Role is FamilyMemberRole.Owner;
}
