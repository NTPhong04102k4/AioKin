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

        // Ruling D5: CanManage phai dan xuat GIONG HET cach SpaceContext dan xuat (Owner HOAC
        // Admin cua Team, HOAC dong-chu-ho gia dinh qua IFamilyContext) — KHONG duoc tu suy dien
        // rieng "nguoi goi la owner" (do la bug ban dau cua brief). Goi lai ISpaceContext cho
        // tung space thay vi lap lai logic, de khong bao gio lech voi SpaceContext.ResolveAsync.
        var result = new List<SpaceResponse>(owned.Count);
        foreach (var space in owned)
        {
            var membership = await _spaceContext.ResolveAsync(space.SpaceUUID, cancellationToken);
            result.Add(SpaceResponse.From(space, canManage: membership?.CanManage ?? false));
        }

        return result;
    }

    public async Task<OperationResult> ListMembersAsync(Guid spaceUuid, CancellationToken cancellationToken = default)
    {
        var space = await _db.Spaces.FirstOrDefaultAsync(s => s.SpaceUUID == spaceUuid && s.SpaceType == SpaceType.Team, cancellationToken);
        if (space is null)
            return OperationResult.Fail("NotFound", "Khong tim thay team.");

        // Bat ky thanh vien nao (Owner/Admin/Member) deu xem duoc danh sach - chi can resolve
        // duoc tu cach thanh vien, KHONG can CanManage (khac AddMemberAsync/RemoveMemberAsync).
        var callerMembership = await _spaceContext.ResolveAsync(spaceUuid, cancellationToken);
        if (callerMembership is null)
            return OperationResult.Fail("Forbidden", "Ban khong phai thanh vien cua team nay.");

        var members = await _db.SpaceMembers
            .AsNoTracking()
            .Where(m => m.SpaceID == space.SpaceID)
            .Include(m => m.User)
            .OrderBy(m => m.JoinedDate)
            .Select(m => SpaceMemberResponse.From(m, m.User!))
            .ToListAsync(cancellationToken);

        return OperationResult.Ok(data: members);
    }

    public async Task<OperationResult> RemoveMemberAsync(Guid spaceUuid, Guid targetUserUuid, CancellationToken cancellationToken = default)
    {
        var space = await _db.Spaces.FirstOrDefaultAsync(s => s.SpaceUUID == spaceUuid && s.SpaceType == SpaceType.Team, cancellationToken);
        if (space is null)
            return OperationResult.Fail("NotFound", "Khong tim thay team.");

        // Danh tinh nguoi goi LUON lay tu token qua ISpaceContext, giong AddMemberAsync (D1).
        var callerMembership = await _spaceContext.ResolveAsync(spaceUuid, cancellationToken);
        if (callerMembership is null)
            return OperationResult.Fail("Forbidden", "Ban khong phai thanh vien cua team nay.");

        // Expo gap G3: tu xoa chinh minh ("roi team") luon duoc phep bat ke CanManage. Xoa
        // nguoi khac thi can CanManage (Owner/Admin) - khong co pattern "leave family" san co
        // trong FamiliesController/FamilyService de theo, nen dung kiem tra don gian nhat o day.
        var isSelfLeave = callerMembership.UserUUID == targetUserUuid;
        if (!isSelfLeave && !callerMembership.CanManage)
            return OperationResult.Fail("Forbidden", "Ban khong co quyen xoa thanh vien nay.");

        var target = await _db.Users.FirstOrDefaultAsync(u => u.UserUUID == targetUserUuid, cancellationToken);
        if (target is null)
            return OperationResult.Fail("UserNotFound", "Khong tim thay nguoi dung.");

        var membership = await _db.SpaceMembers.FirstOrDefaultAsync(
            m => m.SpaceID == space.SpaceID && m.UserID == target.UserID, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("NotFound", "Nguoi nay khong o trong team.");

        _db.SpaceMembers.Remove(membership);
        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Ok("Da xoa thanh vien.");
    }
}
