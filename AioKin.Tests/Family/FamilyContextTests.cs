using AioKin.Data.Entities.Family;
using AioKin.Data.Entities.Security;
using AioKin.Services.Family;
using AioKin.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Xunit;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Tests.Family;

[Collection(ApiCollection.Name)]
public class FamilyContextTests
{
    private readonly ApiFixture _fixture;

    public FamilyContextTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Dung scope rieng va nhet thang mot ClaimsPrincipal vao IHttpContextAccessor: test nay
    /// kiem tra logic phan giai tu cach thanh vien, khong phai duong HTTP.
    /// </summary>
    private static IFamilyContext ContextFor(IServiceScope scope, Guid userUuid)
    {
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userUuid.ToString())], "test"))
        };

        return scope.ServiceProvider.GetRequiredService<IFamilyContext>();
    }

    private static async Task<(User User, FamilyDb Family)> SeedFamilyAsync(
        IServiceScope scope, FamilyMemberRole role)
    {
        var db = ApiFixture.Db(scope);
        var user = TestData.NewUser();
        var family = new FamilyDb { Name = "Nha ctx", OwnerUserID = user.UserID };

        db.Users.Add(user);
        db.Families.Add(family);
        db.FamilyMembers.Add(new FamilyMember
        {
            FamilyID = family.FamilyID,
            UserID = user.UserID,
            MemberRole = role
        });
        await db.SaveChangesAsync();

        return (user, family);
    }

    [Fact]
    public async Task Thanh_vien_thi_phan_giai_ra_dung_vai_tro()
    {
        using var scope = _fixture.CreateScope();
        var (user, family) = await SeedFamilyAsync(scope, FamilyMemberRole.Adult);

        var membership = await ContextFor(scope, user.UserUUID).ResolveAsync(family.FamilyUUID);

        Assert.NotNull(membership);
        Assert.Equal(FamilyMemberRole.Adult, membership!.Role);
        Assert.Equal(family.FamilyID, membership.FamilyID);
        Assert.Equal(user.UserID, membership.UserID);
        Assert.True(membership.CanInvite);
        Assert.False(membership.IsOwner);
    }

    [Fact]
    public async Task Nguoi_ngoai_thi_tra_null()
    {
        using var scope = _fixture.CreateScope();
        var (_, family) = await SeedFamilyAsync(scope, FamilyMemberRole.Owner);

        var db = ApiFixture.Db(scope);
        var outsider = TestData.NewUser();
        db.Users.Add(outsider);
        await db.SaveChangesAsync();

        Assert.Null(await ContextFor(scope, outsider.UserUUID).ResolveAsync(family.FamilyUUID));
    }

    [Fact]
    public async Task Thanh_vien_bi_vo_hieu_hoa_thi_khong_con_la_thanh_vien()
    {
        using var scope = _fixture.CreateScope();
        var (user, family) = await SeedFamilyAsync(scope, FamilyMemberRole.Child);

        var db = ApiFixture.Db(scope);
        var member = db.FamilyMembers.Single(m => m.FamilyID == family.FamilyID && m.UserID == user.UserID);
        member.IsActive = false;
        await db.SaveChangesAsync();

        Assert.Null(await ContextFor(scope, user.UserUUID).ResolveAsync(family.FamilyUUID));
    }

    [Fact]
    public async Task Go_thanh_vien_roi_thi_cache_khong_con_giu_quyen_cu()
    {
        using var scope = _fixture.CreateScope();
        var (user, family) = await SeedFamilyAsync(scope, FamilyMemberRole.Adult);
        var context = ContextFor(scope, user.UserUUID);

        // Lan dau: nap vao cache.
        Assert.NotNull(await context.ResolveAsync(family.FamilyUUID));

        var db = ApiFixture.Db(scope);
        db.FamilyMembers.Remove(
            db.FamilyMembers.Single(m => m.FamilyID == family.FamilyID && m.UserID == user.UserID));
        await db.SaveChangesAsync();

        // Khong xoa cache thi nguoi vua bi go van doc duoc du lieu ca nha them 5 phut nua.
        await context.InvalidateAsync(family.FamilyUUID, user.UserUUID);

        Assert.Null(await context.ResolveAsync(family.FamilyUUID));
    }

    [Fact]
    public async Task Gia_dinh_bi_vo_hieu_hoa_thi_thanh_vien_khong_con_phan_giai_duoc()
    {
        using var scope = _fixture.CreateScope();
        var (user, family) = await SeedFamilyAsync(scope, FamilyMemberRole.Adult);

        var db = ApiFixture.Db(scope);
        var familyRow = db.Families.Single(f => f.FamilyID == family.FamilyID);
        familyRow.IsActive = false;
        await db.SaveChangesAsync();

        // Chua goi ResolveAsync lan nao o tren nen cache con trong: neu ai do lo tay bo dieu
        // kien "&& m.Family!.IsActive" khoi cau Where, test nay se that bai vi doc thang tu
        // database chu khong phai vi doc nham cache cu.
        Assert.Null(await ContextFor(scope, user.UserUUID).ResolveAsync(family.FamilyUUID));
    }

    [Fact]
    public async Task Khong_co_token_thi_tra_null_chu_khong_nem_loi()
    {
        using var scope = _fixture.CreateScope();
        var (_, family) = await SeedFamilyAsync(scope, FamilyMemberRole.Owner);

        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();  // khong co claim nao

        var context = scope.ServiceProvider.GetRequiredService<IFamilyContext>();

        Assert.Null(await context.ResolveAsync(family.FamilyUUID));
    }
}
