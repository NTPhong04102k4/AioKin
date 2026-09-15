using AioKin.Data.Entities.Family;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Models.ViewModel.Family;

/// <summary>
/// Mot gia dinh nhin tu phia mot thanh vien cu the — <see cref="MyRole"/> la vai tro cua
/// nguoi dang goi, khong phai mot thuoc tinh cua gia dinh.
/// </summary>
public class FamilyResponse
{
    public Guid FamilyUuid { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Vai tro cua nguoi goi: Owner, Adult hoac Child.</summary>
    public string MyRole { get; set; } = string.Empty;

    public int MemberCount { get; set; }

    public long StorageQuotaBytes { get; set; }

    /// <summary>Epoch millis UTC — dung quy uoc thoi gian cua <c>ScheduleItemResponse</c>.</summary>
    public long CreatedAtMillis { get; set; }

    public static FamilyResponse From(FamilyDb family, FamilyMemberRole myRole, int memberCount) => new()
    {
        FamilyUuid = family.FamilyUUID,
        Name = family.Name,
        MyRole = myRole.ToString(),
        MemberCount = memberCount,
        StorageQuotaBytes = family.StorageQuotaBytes,
        CreatedAtMillis = new DateTimeOffset(
            DateTime.SpecifyKind(family.CreatedDate, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
    };
}
