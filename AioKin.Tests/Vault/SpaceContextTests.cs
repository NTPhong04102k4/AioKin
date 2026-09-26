using AioKin.Data.Entities.Family;
using AioKin.Data.Entities.Vault;
using AioKin.Services.Vault;
using AioKin.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Tests.Vault;

[Collection(ApiCollection.Name)]
public class SpaceContextTests
{
    private readonly ApiFixture _fixture;

    public SpaceContextTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Personal_space_chi_owner_moi_resolve_duoc_va_luon_CanManage()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var owner = TestData.NewUser();
        var stranger = TestData.NewUser();
        db.Users.AddRange(owner, stranger);
        var space = new Space { SpaceType = SpaceType.Personal, Name = "Personal", OwnerUserID = owner.UserID };
        db.Spaces.Add(space);
        await db.SaveChangesAsync();

        var httpContext = TestHttpContext.ForUser(owner.UserUUID);
        var ctx = BuildContext(scope, httpContext);
        var membership = await ctx.ResolveAsync(space.SpaceUUID);

        Assert.NotNull(membership);
        Assert.True(membership!.CanManage);

        var strangerCtx = BuildContext(scope, TestHttpContext.ForUser(stranger.UserUUID));
        Assert.Null(await strangerCtx.ResolveAsync(space.SpaceUUID));
    }

    [Fact]
    public async Task Family_space_uy_quyen_dung_cho_IFamilyContext_khong_dung_space_members()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var owner = TestData.NewUser();
        db.Users.Add(owner);
        var family = new FamilyDb { Name = "Nha A", OwnerUserID = owner.UserID };
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember { FamilyID = family.FamilyID, UserID = owner.UserID, MemberRole = FamilyMemberRole.Owner });
        var space = new Space { SpaceType = SpaceType.Family, Name = "Nha A", OwnerUserID = owner.UserID, FamilyID = family.FamilyID };
        db.Spaces.Add(space);
        await db.SaveChangesAsync();

        var ctx = BuildContext(scope, TestHttpContext.ForUser(owner.UserUUID));
        var membership = await ctx.ResolveAsync(space.SpaceUUID);

        Assert.NotNull(membership);
        Assert.Equal(SpaceType.Family, membership!.SpaceType);
        Assert.True(membership.CanManage);
    }

    [Fact]
    public async Task Team_space_dung_space_members_khong_dung_family()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var owner = TestData.NewUser();
        var member = TestData.NewUser();
        db.Users.AddRange(owner, member);
        var space = new Space { SpaceType = SpaceType.Team, Name = "Team A", OwnerUserID = owner.UserID };
        db.Spaces.Add(space);
        db.SpaceMembers.Add(new SpaceMember { SpaceID = space.SpaceID, UserID = member.UserID, MemberRole = SpaceMemberRole.Member });
        await db.SaveChangesAsync();

        var memberCtx = BuildContext(scope, TestHttpContext.ForUser(member.UserUUID));
        var membership = await memberCtx.ResolveAsync(space.SpaceUUID);

        Assert.NotNull(membership);
        Assert.False(membership!.CanManage);
    }

    [Fact]
    public async Task Team_space_Admin_role_thi_CanManage()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var owner = TestData.NewUser();
        var admin = TestData.NewUser();
        db.Users.AddRange(owner, admin);
        var space = new Space { SpaceType = SpaceType.Team, Name = "Team B", OwnerUserID = owner.UserID };
        db.Spaces.Add(space);
        db.SpaceMembers.Add(new SpaceMember { SpaceID = space.SpaceID, UserID = admin.UserID, MemberRole = SpaceMemberRole.Admin });
        await db.SaveChangesAsync();

        var adminCtx = BuildContext(scope, TestHttpContext.ForUser(admin.UserUUID));
        var membership = await adminCtx.ResolveAsync(space.SpaceUUID);

        Assert.NotNull(membership);
        Assert.True(membership!.CanManage);
    }

    /// <summary>
    /// Carried tu review Task 2: thanh vien khong phai chu ho (Adult, khong phai Owner)
    /// resolve duoc Family space nhung CanManage phai la false — Task 3/4 dua vao dan xuat
    /// nay (D1: chi Owner/Admin/dong-chu-ho moi quan ly duoc).
    /// </summary>
    [Fact]
    public async Task Family_space_thanh_vien_khong_phai_Owner_thi_CanManage_false()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var owner = TestData.NewUser();
        var adult = TestData.NewUser();
        db.Users.AddRange(owner, adult);
        var family = new FamilyDb { Name = "Nha D", OwnerUserID = owner.UserID };
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember { FamilyID = family.FamilyID, UserID = owner.UserID, MemberRole = FamilyMemberRole.Owner });
        db.FamilyMembers.Add(new FamilyMember { FamilyID = family.FamilyID, UserID = adult.UserID, MemberRole = FamilyMemberRole.Adult });
        var space = new Space { SpaceType = SpaceType.Family, Name = family.Name, OwnerUserID = owner.UserID, FamilyID = family.FamilyID };
        db.Spaces.Add(space);
        await db.SaveChangesAsync();

        var ctx = BuildContext(scope, TestHttpContext.ForUser(adult.UserUUID));
        var membership = await ctx.ResolveAsync(space.SpaceUUID);

        Assert.NotNull(membership);
        Assert.Equal(SpaceType.Family, membership!.SpaceType);
        Assert.False(membership.CanManage);
    }

    [Fact]
    public async Task Family_space_khong_phai_thanh_vien_gia_dinh_thi_tra_null()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var owner = TestData.NewUser();
        var outsider = TestData.NewUser();
        db.Users.AddRange(owner, outsider);
        var family = new FamilyDb { Name = "Nha B", OwnerUserID = owner.UserID };
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember { FamilyID = family.FamilyID, UserID = owner.UserID, MemberRole = FamilyMemberRole.Owner });
        var space = new Space { SpaceType = SpaceType.Family, Name = "Nha B", OwnerUserID = owner.UserID, FamilyID = family.FamilyID };
        db.Spaces.Add(space);
        await db.SaveChangesAsync();

        var outsiderCtx = BuildContext(scope, TestHttpContext.ForUser(outsider.UserUUID));
        Assert.Null(await outsiderCtx.ResolveAsync(space.SpaceUUID));
    }

    [Fact]
    public async Task Khong_co_token_thi_tra_null_chu_khong_nem_loi()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var owner = TestData.NewUser();
        db.Users.Add(owner);
        var space = new Space { SpaceType = SpaceType.Personal, Name = "Personal", OwnerUserID = owner.UserID };
        db.Spaces.Add(space);
        await db.SaveChangesAsync();

        var ctx = BuildContext(scope, new Microsoft.AspNetCore.Http.DefaultHttpContext());
        Assert.Null(await ctx.ResolveAsync(space.SpaceUUID));
    }

    [Fact]
    public async Task Space_khong_ton_tai_thi_tra_null()
    {
        using var scope = _fixture.CreateScope();
        var user = TestData.NewUser();
        var db = ApiFixture.Db(scope);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var ctx = BuildContext(scope, TestHttpContext.ForUser(user.UserUUID));
        Assert.Null(await ctx.ResolveAsync(Guid.NewGuid()));
    }

    private static SpaceContext BuildContext(IServiceScope scope, Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        var accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = httpContext };
        return new SpaceContext(
            ApiFixture.Db(scope),
            scope.ServiceProvider.GetRequiredService<AioKin.Services.Common.Cache.IRedisService>(),
            scope.ServiceProvider.GetRequiredService<AioKin.Services.Family.IFamilyContext>(),
            accessor);
    }
}
