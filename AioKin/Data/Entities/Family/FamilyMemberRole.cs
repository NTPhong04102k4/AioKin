namespace AioKin.Data.Entities.Family;

/// <summary>
/// Vai tro trong pham vi mot gia dinh. KHONG phai role he thong: role trong token van la
/// <see cref="AioKin.Common.Roles.CUSTOMER"/>. Hai khai niem nay doc lap nhau — mot nguoi
/// la Customer o tang he thong va dong thoi la Owner o nha minh, Child o nha bo me.
/// </summary>
public enum FamilyMemberRole
{
    Owner = 0,
    Adult = 1,
    Child = 2
}
