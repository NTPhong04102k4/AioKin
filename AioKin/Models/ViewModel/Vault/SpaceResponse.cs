using AioKin.Data.Entities.Vault;

namespace AioKin.Models.ViewModel.Vault;

public class SpaceResponse
{
    public Guid SpaceUuid { get; set; }
    public string SpaceType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool CanManage { get; set; }
    public long CreatedAtMillis { get; set; }

    public static SpaceResponse From(Space space, bool canManage) => new()
    {
        SpaceUuid = space.SpaceUUID,
        SpaceType = space.SpaceType.ToString(),
        Name = space.Name,
        CanManage = canManage,
        CreatedAtMillis = new DateTimeOffset(DateTime.SpecifyKind(space.CreatedDate, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
    };
}
