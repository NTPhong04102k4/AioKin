using AioKin.Data.Entities.Family;

namespace AioKin.Models.ViewModel.Family;

public class FamilyInviteResponse
{
    public string Code { get; set; } = string.Empty;

    public long ExpiresAtMillis { get; set; }

    public int MaxUses { get; set; }

    public int UsedCount { get; set; }

    public static FamilyInviteResponse From(FamilyInvite invite) => new()
    {
        Code = invite.Code,
        ExpiresAtMillis = new DateTimeOffset(
            DateTime.SpecifyKind(invite.ExpiresAt, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
        MaxUses = invite.MaxUses,
        UsedCount = invite.UsedCount
    };
}
