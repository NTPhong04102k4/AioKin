using AioKin.Common;
using AioKin.Data;
using AioKin.Data.Entities.Vault;
using AioKin.Services.Common.Cache;
using AioKin.Services.Family;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Vault;

public class SpaceContext : ISpaceContext
{
    private readonly AioKinDbContext _db;
    private readonly IFamilyContext _familyContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // IRedisService nhan qua constructor nhung KHONG giu lai thanh field: Space resolve
    // khong tu cache trong plan nay (xem InvalidateAsync ben duoi). Tham so nay ton tai de
    // khop chu ky voi FamilyContext va de mo rong cache sau nay ma khong doi signature —
    // giu lam field se sinh CS0414 "assigned but never used" vi khong doc lai o dau ca.
    public SpaceContext(
        AioKinDbContext db,
        IRedisService redis,
        IFamilyContext familyContext,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _familyContext = familyContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<SpaceMembership?> ResolveAsync(Guid spaceUuid, CancellationToken cancellationToken = default)
    {
        // Danh tinh LUON lay tu token, giong het IFamilyContext.
        var userUuid = _httpContextAccessor.HttpContext?.User.GetUserUuid();
        if (userUuid is null)
            return null;

        var space = await _db.Spaces
            .AsNoTracking()
            .Include(s => s.Family)
            .FirstOrDefaultAsync(s => s.SpaceUUID == spaceUuid && s.IsActive, cancellationToken);

        if (space is null)
            return null;

        return space.SpaceType switch
        {
            SpaceType.Personal => await ResolvePersonalAsync(space, userUuid.Value, cancellationToken),
            SpaceType.Family => await ResolveFamilyAsync(space, userUuid.Value, cancellationToken),
            SpaceType.Team => await ResolveTeamAsync(space, userUuid.Value, cancellationToken),
            _ => null
        };
    }

    private async Task<SpaceMembership?> ResolvePersonalAsync(Space space, Guid userUuid, CancellationToken cancellationToken)
    {
        // Personal khong co bang thanh vien: chinh chu la thanh vien duy nhat. Can doi
        // OwnerUserID (noi bo) sang UserUUID de so sanh — tra ve null neu khong khop thay vi
        // truy van them, vi resolve that bai o day rat re (chi mot truy van).
        var isOwner = await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.UserID == space.OwnerUserID && u.UserUUID == userUuid, cancellationToken);

        return isOwner
            ? new SpaceMembership(space.SpaceID, space.SpaceUUID, space.SpaceType, space.OwnerUserID, userUuid, CanManage: true)
            : null;
    }

    private async Task<SpaceMembership?> ResolveFamilyAsync(Space space, Guid userUuid, CancellationToken cancellationToken)
    {
        if (space.Family is null)
            return null;

        // Uy quyen hoan toan cho IFamilyContext — KHONG tu query family_members o day.
        var familyMembership = await _familyContext.ResolveAsync(space.Family.FamilyUUID, cancellationToken);
        if (familyMembership is null)
            return null;

        return new SpaceMembership(
            space.SpaceID, space.SpaceUUID, space.SpaceType,
            familyMembership.UserID, userUuid,
            CanManage: familyMembership.IsOwner);
    }

    private async Task<SpaceMembership?> ResolveTeamAsync(Space space, Guid userUuid, CancellationToken cancellationToken)
    {
        var row = await _db.SpaceMembers
            .AsNoTracking()
            .Where(m => m.SpaceID == space.SpaceID && m.User!.UserUUID == userUuid)
            .Select(m => new { m.UserID, m.MemberRole })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        return new SpaceMembership(
            space.SpaceID, space.SpaceUUID, space.SpaceType,
            row.UserID, userUuid,
            CanManage: row.MemberRole is SpaceMemberRole.Owner or SpaceMemberRole.Admin);
    }

    public Task InvalidateAsync(Guid spaceUuid, Guid userUuid, CancellationToken cancellationToken = default)
        // Space resolve khong cache rieng trong plan nay — no doc thang tu Spaces/SpaceMembers
        // (re) hoac uy quyen cho IFamilyContext (co cache rieng, tu invalidate qua duong cua
        // no). Giu ham nay de khop interface va de mo rong cache sau nay ma khong doi chu ky.
        => Task.CompletedTask;
}
