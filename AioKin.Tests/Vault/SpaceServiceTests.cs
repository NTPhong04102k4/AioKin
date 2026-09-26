using AioKin.Data.Entities.Family;
using AioKin.Data.Entities.Vault;
using AioKin.Models.InputModel.Vault;
using AioKin.Services.Family;
using AioKin.Services.Vault;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Tests.Vault;

[Collection(ApiCollection.Name)]
public class SpaceServiceTests
{
    private readonly ApiFixture _fixture;

    public SpaceServiceTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task EnsureMyPersonalSpaceAsync_tao_dung_1_lan_du_goi_nhieu_lan()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = scope.ServiceProvider.GetRequiredService<ISpaceService>();

        var first = await sut.EnsureMyPersonalSpaceAsync(user.UserUUID);
        var second = await sut.EnsureMyPersonalSpaceAsync(user.UserUUID);

        Assert.Equal(first.SpaceUuid, second.SpaceUuid);
        Assert.Equal(1, await db.Spaces.CountAsync(s => s.OwnerUserID == user.UserID && s.SpaceType == SpaceType.Personal));
    }

    [Fact]
    public async Task CreateTeamAsync_nguoi_tao_thanh_Owner()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = scope.ServiceProvider.GetRequiredService<ISpaceService>();
        var result = await sut.CreateTeamAsync(user.UserUUID, new CreateTeamSpaceRequest { Name = "Team A" });

        Assert.True(result.Success);
        // Loc them theo OwnerUserID: DB test dung chung (ApiCollection) tich luy Team space
        // tu cac test khac trong cung collection, nen FirstAsync khong loc gi them co the
        // vo tinh trung mot team CU cua test khac chu khong phai team vua tao o day.
        var space = await db.Spaces.FirstAsync(s => s.SpaceType == SpaceType.Team && s.OwnerUserID == user.UserID);
        var membership = await db.SpaceMembers.FirstAsync(m => m.SpaceID == space.SpaceID && m.UserID == user.UserID);
        Assert.Equal(SpaceMemberRole.Owner, membership.MemberRole);
    }

    [Fact]
    public async Task AddMemberAsync_Owner_them_duoc_thanh_vien()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var owner = TestData.NewUser();
        var target = TestData.NewUser();
        db.Users.AddRange(owner, target);
        var space = new Space { SpaceType = SpaceType.Team, Name = "Team B", OwnerUserID = owner.UserID };
        db.Spaces.Add(space);
        db.SpaceMembers.Add(new SpaceMember { SpaceID = space.SpaceID, UserID = owner.UserID, MemberRole = SpaceMemberRole.Owner });
        await db.SaveChangesAsync();

        var sut = BuildService(scope, owner.UserUUID);
        var result = await sut.AddMemberAsync(space.SpaceUUID, new AddSpaceMemberRequest { UserCode = target.UserCode });

        Assert.True(result.Success);
        Assert.True(await db.SpaceMembers.AnyAsync(m => m.SpaceID == space.SpaceID && m.UserID == target.UserID));
    }

    /// <summary>
    /// Ruling D1 (SECURITY): thanh vien thuong (khong phai Owner/Admin) khong duoc them
    /// nguoi khac vao team. AddMemberAsync phai tu choi (Forbidden), khong duoc nem loi, va
    /// khong duoc ghi ban ghi SpaceMember nao ca.
    /// </summary>
    [Fact]
    public async Task AddMemberAsync_khong_phai_manager_thi_bi_tu_choi()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var owner = TestData.NewUser();
        var plainMember = TestData.NewUser();
        var target = TestData.NewUser();
        db.Users.AddRange(owner, plainMember, target);
        var space = new Space { SpaceType = SpaceType.Team, Name = "Team C", OwnerUserID = owner.UserID };
        db.Spaces.Add(space);
        db.SpaceMembers.Add(new SpaceMember { SpaceID = space.SpaceID, UserID = plainMember.UserID, MemberRole = SpaceMemberRole.Member });
        await db.SaveChangesAsync();

        var sut = BuildService(scope, plainMember.UserUUID);
        var result = await sut.AddMemberAsync(space.SpaceUUID, new AddSpaceMemberRequest { UserCode = target.UserCode });

        Assert.False(result.Success);
        Assert.Equal("Forbidden", result.ErrorCode);
        Assert.False(await db.SpaceMembers.AnyAsync(m => m.SpaceID == space.SpaceID && m.UserID == target.UserID));
    }

    /// <summary>Ruling D1: nguoi khong o trong team nay chut nao (khong resolve duoc membership) cung bi tu choi.</summary>
    [Fact]
    public async Task AddMemberAsync_nguoi_ngoai_team_thi_bi_tu_choi()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var owner = TestData.NewUser();
        var outsider = TestData.NewUser();
        var target = TestData.NewUser();
        db.Users.AddRange(owner, outsider, target);
        var space = new Space { SpaceType = SpaceType.Team, Name = "Team D", OwnerUserID = owner.UserID };
        db.Spaces.Add(space);
        db.SpaceMembers.Add(new SpaceMember { SpaceID = space.SpaceID, UserID = owner.UserID, MemberRole = SpaceMemberRole.Owner });
        await db.SaveChangesAsync();

        var sut = BuildService(scope, outsider.UserUUID);
        var result = await sut.AddMemberAsync(space.SpaceUUID, new AddSpaceMemberRequest { UserCode = target.UserCode });

        Assert.False(result.Success);
        Assert.Equal("Forbidden", result.ErrorCode);
    }

    [Fact]
    public async Task GetMineAsync_bao_gom_personal_family_va_team()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var user = TestData.NewUser();
        db.Users.Add(user);
        var family = new FamilyDb { Name = "Nha C", OwnerUserID = user.UserID };
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember { FamilyID = family.FamilyID, UserID = user.UserID, MemberRole = FamilyMemberRole.Owner });
        db.Spaces.Add(new Space { SpaceType = SpaceType.Family, Name = family.Name, OwnerUserID = user.UserID, FamilyID = family.FamilyID });
        await db.SaveChangesAsync();

        var sut = scope.ServiceProvider.GetRequiredService<ISpaceService>();
        await sut.CreateTeamAsync(user.UserUUID, new CreateTeamSpaceRequest { Name = "Team E" });

        var mine = await sut.GetMineAsync(user.UserUUID);

        Assert.Contains(mine, s => s.SpaceType == SpaceType.Personal.ToString());
        Assert.Contains(mine, s => s.SpaceType == SpaceType.Family.ToString());
        Assert.Contains(mine, s => s.SpaceType == SpaceType.Team.ToString());
    }

    private static SpaceService BuildService(IServiceScope scope, Guid callerUserUuid)
    {
        var db = ApiFixture.Db(scope);
        var accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = TestHttpContext.ForUser(callerUserUuid) };
        var spaceContext = new SpaceContext(
            db,
            scope.ServiceProvider.GetRequiredService<AioKin.Services.Common.Cache.IRedisService>(),
            scope.ServiceProvider.GetRequiredService<IFamilyContext>(),
            accessor);

        return new SpaceService(db, spaceContext);
    }
}
