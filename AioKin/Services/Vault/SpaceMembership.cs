using AioKin.Data.Entities.Vault;

namespace AioKin.Services.Vault;

/// <summary>
/// Ket qua kiem tra thanh vien, bat ke Space la Personal/Family/Team. CanManage la kha nang
/// duy nhat can cho pham vi plan nay: sua/xoa noi dung cua nguoi khac trong space. Tac gia tu
/// sua/xoa bai cua chinh minh luon duoc phep — kiem tra do nam o tang service goi ham nay,
/// khong nam trong CanManage.
/// </summary>
/// <param name="SpaceID">Id noi bo — dung cho khoa ngoai.</param>
/// <param name="SpaceUUID">Id cong khai.</param>
/// <param name="SpaceType">Loai pham vi — Personal/Family/Team.</param>
/// <param name="UserID">Id noi bo cua nguoi goi.</param>
/// <param name="UserUUID">Id cong khai cua nguoi goi, lay tu token.</param>
/// <param name="CanManage">Co quyen sua/xoa noi dung cua nguoi khac trong space khong.</param>
public sealed record SpaceMembership(
    Guid SpaceID,
    Guid SpaceUUID,
    SpaceType SpaceType,
    Guid UserID,
    Guid UserUUID,
    bool CanManage);
