using AioKin.Data.Entities.Family;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Tests.Family;

[Collection(ApiCollection.Name)]
public class FamilySchemaTests
{
    private readonly ApiFixture _fixture;

    public FamilySchemaTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Mot_nguoi_khong_the_vao_cung_mot_gia_dinh_hai_lan()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        var family = new FamilyDb { Name = "Nha test", OwnerUserID = user.UserID };
        db.Users.Add(user);
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyID = family.FamilyID,
            UserID = user.UserID,
            MemberRole = FamilyMemberRole.Owner
        });
        await db.SaveChangesAsync();

        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyID = family.FamilyID,
            UserID = user.UserID,
            MemberRole = FamilyMemberRole.Adult
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Ma_moi_la_duy_nhat_tren_toan_he_thong()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var owner = TestData.NewUser();
        var a = new FamilyDb { Name = "Nha A", OwnerUserID = owner.UserID };
        var b = new FamilyDb { Name = "Nha B", OwnerUserID = owner.UserID };
        db.Users.Add(owner);
        db.Families.AddRange(a, b);

        var code = $"C{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        db.FamilyInvites.Add(new FamilyInvite
        {
            FamilyID = a.FamilyID,
            Code = code,
            CreatedByUserID = owner.UserID,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            MaxUses = 5
        });
        await db.SaveChangesAsync();

        db.FamilyInvites.Add(new FamilyInvite
        {
            FamilyID = b.FamilyID,
            Code = code,
            CreatedByUserID = owner.UserID,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            MaxUses = 5
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Xoa_gia_dinh_thi_thanh_vien_va_ma_moi_di_theo()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        var family = new FamilyDb { Name = "Nha xoa", OwnerUserID = user.UserID };
        db.Users.Add(user);
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyID = family.FamilyID,
            UserID = user.UserID,
            MemberRole = FamilyMemberRole.Owner
        });
        await db.SaveChangesAsync();

        db.Families.Remove(family);
        await db.SaveChangesAsync();

        Assert.Empty(await db.FamilyMembers.Where(m => m.FamilyID == family.FamilyID).ToListAsync());
    }
}
