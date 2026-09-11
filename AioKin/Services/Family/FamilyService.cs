using AioKin.Data;
using AioKin.Data.Entities.Family;
using AioKin.Models.InputModel.Auth.User;
using AioKin.Models.InputModel.Family;
using AioKin.Models.ViewModel.Family;
using Microsoft.EntityFrameworkCore;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Services.Family;

public class FamilyService : IFamilyService
{
    private readonly AioKinDbContext _db;

    public FamilyService(AioKinDbContext db)
    {
        _db = db;
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
}
