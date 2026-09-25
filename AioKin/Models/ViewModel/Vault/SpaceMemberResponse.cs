using AioKin.Data.Entities.Vault;
using UserDb = AioKin.Data.Entities.Security.User;

namespace AioKin.Models.ViewModel.Vault;

/// <summary>Mot thanh vien trong danh sach GET /spaces/{uuid}/members. Chi dung cho Team.</summary>
public class SpaceMemberResponse
{
    public Guid UserUuid { get; set; }
    public string UserCode { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Role { get; set; } = string.Empty;
    public long JoinedAtMillis { get; set; }

    public static SpaceMemberResponse From(SpaceMember member, UserDb user) => new()
    {
        UserUuid = user.UserUUID,
        UserCode = user.UserCode,
        FullName = user.FullName,
        Role = member.MemberRole.ToString(),
        JoinedAtMillis = new DateTimeOffset(
            DateTime.SpecifyKind(member.JoinedDate, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
    };
}
