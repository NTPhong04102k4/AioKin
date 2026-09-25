using AioKin.Common;
using AioKin.Data;
using AioKin.Data.Entities.Vault;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Vault;
using AioKin.Models.ViewModel.Vault;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Vault;

public class SpaceService : ISpaceService
{
    private readonly AioKinDbContext _db;
    private readonly ISpaceContext _spaceContext;

    public SpaceService(AioKinDbContext db, ISpaceContext spaceContext)
    {
        _db = db;
        _spaceContext = spaceContext;
    }

    public async Task<SpaceResponse> EnsureMyPersonalSpaceAsync(Guid callerUserUuid, CancellationToken cancellationToken = default)
    {
        var userId = await _db.Users.Where(u => u.UserUUID == callerUserUuid).Select(u => u.UserID).FirstAsync(cancellationToken);

        var existing = await _db.Spaces.FirstOrDefaultAsync(
            s => s.OwnerUserID == userId && s.SpaceType == SpaceType.Personal, cancellationToken);

        if (existing is not null)
            return SpaceResponse.From(existing, canManage: true);

        var space = new Space { SpaceType = SpaceType.Personal, Name = "Personal", OwnerUserID = userId };

        try
        {
            _db.Spaces.Add(space);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Race: hai request cung tao personal space cung luc. Unique index (xem
            // AioKinDbContext.OnModelCreating) chan ban ghi thu hai — doc lai ban da thang.
            _db.ChangeTracker.Clear();
            existing = await _db.Spaces.FirstAsync(
                s => s.OwnerUserID == userId && s.SpaceType == SpaceType.Personal, cancellationToken);
            return SpaceResponse.From(existing, canManage: true);
        }

        return SpaceResponse.From(space, canManage: true);
    }

    public async Task<OperationResult> CreateTeamAsync(Guid callerUserUuid, CreateTeamSpaceRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await _db.Users.Where(u => u.UserUUID == callerUserUuid).Select(u => u.UserID).FirstOrDefaultAsync(cancellationToken);
        if (userId == Guid.Empty)
            return OperationResult.Fail("UserNotFound", "Khong tim thay tai khoan.");

        var space = new Space { SpaceType = SpaceType.Team, Name = request.Name.Trim(), OwnerUserID = userId };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Spaces.Add(space);
        _db.SpaceMembers.Add(new SpaceMember { SpaceID = space.SpaceID, UserID = userId, MemberRole = SpaceMemberRole.Owner });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OperationResult.Ok("Da tao team.", SpaceResponse.From(space, canManage: true));
    }

    public async Task<OperationResult> AddMemberAsync(Guid spaceUuid, AddSpaceMemberRequest request, CancellationToken cancellationToken = default)
    {
        var space = await _db.Spaces.FirstOrDefaultAsync(s => s.SpaceUUID == spaceUuid && s.SpaceType == SpaceType.Team, cancellationToken);
        if (space is null)
            return OperationResult.Fail("NotFound", "Khong tim thay team.");

        // Ruling D1 (SECURITY): danh tinh nguoi goi LUON lay tu token qua ISpaceContext,
        // KHONG qua tham so ham (tranh gia mao). Chi Owner/Admin cua chinh team nay
        // (CanManage == true) moi them duoc thanh vien — nguoc lai tu choi, khong nem loi.
        var callerMembership = await _spaceContext.ResolveAsync(spaceUuid, cancellationToken);
        if (callerMembership is null || !callerMembership.CanManage)
            return OperationResult.Fail("Forbidden", "Ban khong co quyen them thanh vien vao team nay.");

        var target = await _db.Users.FirstOrDefaultAsync(u => u.UserCode == request.UserCode, cancellationToken);
        if (target is null)
            return OperationResult.Fail("UserNotFound", "Khong tim thay nguoi dung voi UserCode nay.");

        var alreadyMember = await _db.SpaceMembers.AnyAsync(m => m.SpaceID == space.SpaceID && m.UserID == target.UserID, cancellationToken);
        if (alreadyMember)
            return OperationResult.Fail("Conflict", "Nguoi nay da o trong team.");

        _db.SpaceMembers.Add(new SpaceMember { SpaceID = space.SpaceID, UserID = target.UserID, MemberRole = SpaceMemberRole.Member });
        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Ok("Da them thanh vien.");
    }

    public async Task<IReadOnlyList<SpaceResponse>> GetMineAsync(Guid callerUserUuid, CancellationToken cancellationToken = default)
    {
        await EnsureMyPersonalSpaceAsync(callerUserUuid, cancellationToken);

        var userId = await _db.Users.Where(u => u.UserUUID == callerUserUuid).Select(u => u.UserID).FirstAsync(cancellationToken);

        var owned = await _db.Spaces
            .AsNoTracking()
            .Where(s => s.IsActive && (
                s.OwnerUserID == userId ||
                _db.SpaceMembers.Any(m => m.SpaceID == s.SpaceID && m.UserID == userId) ||
                (s.FamilyID != null && _db.FamilyMembers.Any(fm => fm.FamilyID == s.FamilyID && fm.UserID == userId && fm.IsActive))
            ))
            .OrderByDescending(s => s.CreatedDate)
            .ToListAsync(cancellationToken);

        return [.. owned.Select(s => SpaceResponse.From(s, canManage: s.OwnerUserID == userId))];
    }
}
