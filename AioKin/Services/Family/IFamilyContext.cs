namespace AioKin.Services.Family;

/// <summary>
/// Tra loi duy nhat mot cau hoi: nguoi goi request nay co phai thanh vien cua
/// <c>familyUuid</c> khong, va voi vai tro gi.
///
/// MOI service co pham vi gia dinh phai goi <see cref="ResolveAsync"/> truoc khi cham du
/// lieu. Dat kiem tra o mot cho thay vi lap lai o tung controller: bo sot mot cho la ro ri
/// du lieu ca gia dinh, va mot cho bi bo sot thi khong ai nhin ra khi doc code.
/// </summary>
public interface IFamilyContext
{
    /// <summary>
    /// Tu cach thanh vien cua nguoi dang goi, hoac <c>null</c> neu khong phai thanh vien.
    /// Null cung la ket qua khi token thieu claim danh tinh — goi tra ve
    /// <c>OperationResult.Fail("NotAFamilyMember", ...)</c>.
    /// </summary>
    Task<FamilyMembership?> ResolveAsync(Guid familyUuid, CancellationToken cancellationToken = default);

    /// <summary>Xoa cache cua mot nguoi trong mot gia dinh. Goi ngay khi doi vai tro hoac go thanh vien.</summary>
    Task InvalidateAsync(Guid familyUuid, Guid userUuid, CancellationToken cancellationToken = default);

    /// <summary>Xoa cache cua ca gia dinh. Goi khi gia dinh bi vo hieu hoa hoac xoa.</summary>
    Task InvalidateFamilyAsync(Guid familyUuid, CancellationToken cancellationToken = default);
}
