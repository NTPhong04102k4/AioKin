using AioKin.Data;
using AioKin.Data.Entities.Family;
using AioKin.Data.Entities.Vault;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Family;
using AioKin.Models.ViewModel.Family;
using Microsoft.EntityFrameworkCore;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Services.Family;

public class FamilyService : IFamilyService
{
    private readonly AioKinDbContext _db;
    private readonly IFamilyContext _familyContext;

    public FamilyService(AioKinDbContext db, IFamilyContext familyContext)
    {
        _db = db;
        _familyContext = familyContext;
    }

    public async Task<OperationResult> CreateAsync(
        Guid callerUserUuid,
        CreateFamilyRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
            return OperationResult.Fail("ValidationError", "Ten gia dinh khong duoc de rong.");

        var userId = await _db.Users
            .Where(u => u.UserUUID == callerUserUuid)
            .Select(u => u.UserID)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId == Guid.Empty)
            return OperationResult.Fail("UserNotFound", "Khong tim thay tai khoan.");

        var family = new FamilyDb { Name = name, OwnerUserID = userId };

        // Gia dinh va dong Owner phai cung song hoac cung khong. Mot gia dinh khong co thanh
        // vien nao la mot dong ma khong ai — ke ca nguoi vua tao — cham toi duoc nua.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Families.Add(family);
        _db.FamilyMembers.Add(new FamilyMember
        {
            FamilyID = family.FamilyID,
            UserID = userId,
            MemberRole = FamilyMemberRole.Owner
        });
        _db.Spaces.Add(new Space
        {
            SpaceType = SpaceType.Family,
            Name = family.Name,
            OwnerUserID = userId,
            FamilyID = family.FamilyID
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OperationResult.Ok("Da tao gia dinh.", FamilyResponse.From(family, FamilyMemberRole.Owner, 1));
    }

    public async Task<IReadOnlyList<FamilyResponse>> GetMineAsync(
        Guid callerUserUuid,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.FamilyMembers
            .AsNoTracking()
            .Where(m => m.IsActive && m.User!.UserUUID == callerUserUuid && m.Family!.IsActive)
            .OrderByDescending(m => m.Family!.CreatedDate)
            .Select(m => new
            {
                Family = m.Family!,
                m.MemberRole,
                // Dem trong cung mot lan tra database. Tra ve roi dem trong bo nho nghia la
                // keo toan bo thanh vien cua moi gia dinh ve app chi de lay mot con so.
                MemberCount = m.Family!.Members.Count(x => x.IsActive)
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => FamilyResponse.From(r.Family, r.MemberRole, r.MemberCount))];
    }

    public async Task<OperationResult> CreateInviteAsync(
        Guid familyUuid,
        CreateInviteRequest request,
        CancellationToken cancellationToken = default)
    {
        // Cong kiem tra. Moi duong cham du lieu co pham vi gia dinh bat dau bang dong nay.
        var membership = await _familyContext.ResolveAsync(familyUuid, cancellationToken);
        if (membership is null)
            return OperationResult.Fail("NotAFamilyMember", "Ban khong thuoc gia dinh nay.");

        if (!membership.CanInvite)
            return OperationResult.Fail("Forbidden", "Chi chu ho hoac nguoi lon moi tao duoc ma moi.");

        var invite = new FamilyInvite
        {
            FamilyID = membership.FamilyID,
            Code = InviteCodeGenerator.Next(),
            CreatedByUserID = membership.UserID,
            ExpiresAt = DateTime.UtcNow.AddHours(request.ExpiresInHours),
            MaxUses = request.MaxUses
        };

        _db.FamilyInvites.Add(invite);
        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Ok("Da tao ma moi.", FamilyInviteResponse.From(invite));
    }

    public async Task<OperationResult> JoinAsync(
        Guid callerUserUuid,
        JoinFamilyRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        var userId = await _db.Users
            .Where(u => u.UserUUID == callerUserUuid)
            .Select(u => u.UserID)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId == Guid.Empty)
            return OperationResult.Fail("UserNotFound", "Khong tim thay tai khoan.");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var invite = await _db.FamilyInvites
            .Include(i => i.Family)
            .FirstOrDefaultAsync(i => i.Code == code, cancellationToken);

        // Ma het han, dung het luot, bi thu hoi va ma khong ton tai deu tra ve cung mot cau
        // tra loi. Phan biet chung nghia la noi cho nguoi do rang ma nay TUNG dung duoc —
        // du de biet minh doan gan trung va nen doan tiep.
        if (invite is null
            || invite.Family is null
            || !invite.Family.IsActive
            || !invite.IsUsable(DateTime.UtcNow))
        {
            return OperationResult.Fail("NotFound", "Ma moi khong hop le hoac da het han.");
        }

        var existing = await _db.FamilyMembers
            .FirstOrDefaultAsync(m => m.FamilyID == invite.FamilyID && m.UserID == userId, cancellationToken);

        if (existing is not null)
        {
            // Da o trong nha roi. Bam nham lan hai la chuyen binh thuong, va no khong duoc
            // an mot luot cua ma moi.
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                existing.JoinedDate = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                await _familyContext.InvalidateAsync(invite.Family.FamilyUUID, callerUserUuid, cancellationToken);
            }
            else
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return OperationResult.Ok("Ban da o trong gia dinh nay.");
        }

        _db.FamilyMembers.Add(new FamilyMember
        {
            FamilyID = invite.FamilyID,
            UserID = userId,
            MemberRole = FamilyMemberRole.Adult
        });

        // Dem luot va tao dong thanh vien phai cung thanh cong hoac cung that bai — day la
        // ly do ca hai nam trong mot transaction.
        invite.UsedCount += 1;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OperationResult.Ok("Da vao gia dinh.");
    }
}
