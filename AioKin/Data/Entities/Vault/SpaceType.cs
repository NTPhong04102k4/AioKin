namespace AioKin.Data.Entities.Vault;

/// <summary>
/// Loai pham vi chia se. Family khong tao lai co che thanh vien — no tro toi
/// family.families qua FamilyID va uy quyen cau hoi thanh vien cho IFamilyContext.
/// </summary>
public enum SpaceType { Personal = 0, Family = 1, Team = 2 }
