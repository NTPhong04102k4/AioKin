namespace AioKin.Services.Vault;

/// <summary>
/// Tra loi duy nhat mot cau hoi cho ca 3 loai Space: nguoi goi request nay co phai thanh
/// vien cua spaceUuid khong, va co quyen quan ly khong. Moi service cham du lieu Prompt
/// domain phai goi ResolveAsync truoc, giong het cach IFamilyContext gate M0.
/// </summary>
public interface ISpaceContext
{
    Task<SpaceMembership?> ResolveAsync(Guid spaceUuid, CancellationToken cancellationToken = default);

    Task InvalidateAsync(Guid spaceUuid, Guid userUuid, CancellationToken cancellationToken = default);
}
