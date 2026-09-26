using System.Net;
using System.Net.Http.Json;
using AioKin.Data.Entities.Family;
using AioKin.Data.Entities.Vault;
using AioKin.Models.ViewModel.Vault;
using AioKin.Tests.Auth;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FamilyDb = AioKin.Data.Entities.Family.Family;

namespace AioKin.Tests.Vault;

/// <summary>
/// /spaces/*: pham vi chia se cho Prompt/Category/Tag. Ruling D5 - CanManage tren GET
/// /spaces/me phai dan xuat GIONG HET ISpaceContext (Owner/Admin cua Team, hoac dong-chu-ho
/// gia dinh), khong duoc suy dien rieng "nguoi goi la owner". Expo gap G3 - GET/DELETE
/// /spaces/{uuid}/members/{userUuid}, voi tu-xoa-chinh-minh ("roi team") luon duoc phep.
/// </summary>
[Collection(ApiCollection.Name)]
public class SpaceEndpointTests
{
    private readonly ApiFixture _fixture;

    public SpaceEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetMine_luon_co_it_nhat_1_personal_space()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var response = await testUser.Client.GetAsync("/spaces/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<List<SpaceResponse>>>();
        Assert.Contains(body!.Data!, s => s.SpaceType == "Personal");
    }

    [Fact]
    public async Task CreateTeam_roi_GetMine_thay_team_do()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var create = await testUser.Client.PostAsJsonAsync("/spaces/team", new { name = "Team A" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var mine = await testUser.Client.GetFromJsonAsync<OperationResultOf<List<SpaceResponse>>>("/spaces/me");
        Assert.Contains(mine!.Data!, s => s.Name == "Team A" && s.SpaceType == "Team");
    }

    /// <summary>
    /// Ruling D5: mot Admin (khong phai Owner cua team) van phai thay CanManage = true tren
    /// GET /spaces/me. Truoc fix, code cu chi so sanh OwnerUserID nen Admin se bi bao sai
    /// thanh CanManage = false.
    /// </summary>
    [Fact]
    public async Task GetMine_Team_Admin_thi_CanManage_true()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var admin = await TestUser.CreateAsync(_fixture);

        var create = await owner.Client.PostAsJsonAsync("/spaces/team", new { name = "Team Admin" });
        var created = await create.Content.ReadFromJsonAsync<OperationResultOf<SpaceResponse>>();
        var spaceUuid = created!.Data!.SpaceUuid;

        await owner.Client.PostAsJsonAsync($"/spaces/{spaceUuid}/members", new { userCode = admin.UserCode });

        // Khong co endpoint doi vai tro trong pham vi task nay - nang thang len Admin qua DB.
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var membership = await db.SpaceMembers.FirstAsync(m => m.UserID == admin.UserId);
            membership.MemberRole = SpaceMemberRole.Admin;
            await db.SaveChangesAsync();
        }

        var mine = await admin.Client.GetFromJsonAsync<OperationResultOf<List<SpaceResponse>>>("/spaces/me");
        var teamEntry = Assert.Single(mine!.Data!, s => s.SpaceUuid == spaceUuid);
        Assert.True(teamEntry.CanManage);
    }

    /// <summary>
    /// Ruling D5: mot dong-chu-ho gia dinh (FamilyMemberRole.Owner nhung KHONG phai
    /// Space.OwnerUserID - truong nay chi ghi nguoi tao gia dinh) van phai thay CanManage =
    /// true tren space Family cua minh. Day chinh la kich ban bug goc cua brief.
    /// </summary>
    [Fact]
    public async Task GetMine_Family_dong_chu_ho_thi_CanManage_true()
    {
        var creator = await TestUser.CreateAsync(_fixture);
        var coOwner = await TestUser.CreateAsync(_fixture);

        Guid familySpaceUuid;
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var creatorRow = await db.Users.FirstAsync(u => u.UserUUID == creator.UserUuid);
            var coOwnerRow = await db.Users.FirstAsync(u => u.UserUUID == coOwner.UserUuid);

            var family = new FamilyDb { Name = "Nha Dong Chu Ho", OwnerUserID = creatorRow.UserID };
            db.Families.Add(family);
            db.FamilyMembers.Add(new FamilyMember { FamilyID = family.FamilyID, UserID = creatorRow.UserID, MemberRole = FamilyMemberRole.Owner });
            db.FamilyMembers.Add(new FamilyMember { FamilyID = family.FamilyID, UserID = coOwnerRow.UserID, MemberRole = FamilyMemberRole.Owner });
            var familySpace = new Space { SpaceType = SpaceType.Family, Name = family.Name, OwnerUserID = creatorRow.UserID, FamilyID = family.FamilyID };
            db.Spaces.Add(familySpace);
            await db.SaveChangesAsync();
            familySpaceUuid = familySpace.SpaceUUID;
        }

        var mine = await coOwner.Client.GetFromJsonAsync<OperationResultOf<List<SpaceResponse>>>("/spaces/me");
        var familyEntry = Assert.Single(mine!.Data!, s => s.SpaceUuid == familySpaceUuid);
        Assert.True(familyEntry.CanManage);
    }

    /// <summary>Expo gap G3: bat ky thanh vien nao (khong chi manager) deu xem duoc danh sach.</summary>
    [Fact]
    public async Task GetMembers_bat_ky_thanh_vien_nao_cung_xem_duoc()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var plainMember = await TestUser.CreateAsync(_fixture);

        var create = await owner.Client.PostAsJsonAsync("/spaces/team", new { name = "Team View" });
        var created = await create.Content.ReadFromJsonAsync<OperationResultOf<SpaceResponse>>();
        var spaceUuid = created!.Data!.SpaceUuid;
        await owner.Client.PostAsJsonAsync($"/spaces/{spaceUuid}/members", new { userCode = plainMember.UserCode });

        var response = await plainMember.Client.GetAsync($"/spaces/{spaceUuid}/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<List<SpaceMemberResponse>>>();
        Assert.Equal(2, body!.Data!.Count);
        Assert.Contains(body.Data, m => m.UserUuid == owner.UserUuid);
        Assert.Contains(body.Data, m => m.UserUuid == plainMember.UserUuid);
    }

    /// <summary>
    /// Isolation gia dinh vs team (Review Focus): endpoint danh cho thanh vien Team khong duoc
    /// lo du lieu cua mot Family space - phai tra NotFound (ListMembersAsync chi xu ly Team).
    /// </summary>
    [Fact]
    public async Task GetMembers_tren_Family_space_tra_NotFound_khong_lo_du_lieu()
    {
        var owner = await TestUser.CreateAsync(_fixture);

        Guid familySpaceUuid;
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var ownerRow = await db.Users.FirstAsync(u => u.UserUUID == owner.UserUuid);
            var family = new FamilyDb { Name = "Nha Isolation", OwnerUserID = ownerRow.UserID };
            db.Families.Add(family);
            db.FamilyMembers.Add(new FamilyMember { FamilyID = family.FamilyID, UserID = ownerRow.UserID, MemberRole = FamilyMemberRole.Owner });
            var familySpace = new Space { SpaceType = SpaceType.Family, Name = family.Name, OwnerUserID = ownerRow.UserID, FamilyID = family.FamilyID };
            db.Spaces.Add(familySpace);
            await db.SaveChangesAsync();
            familySpaceUuid = familySpace.SpaceUUID;
        }

        var response = await owner.Client.GetAsync($"/spaces/{familySpaceUuid}/members");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMember_manager_xoa_duoc_thanh_vien_khac()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var member = await TestUser.CreateAsync(_fixture);

        var create = await owner.Client.PostAsJsonAsync("/spaces/team", new { name = "Team Del1" });
        var created = await create.Content.ReadFromJsonAsync<OperationResultOf<SpaceResponse>>();
        var spaceUuid = created!.Data!.SpaceUuid;
        await owner.Client.PostAsJsonAsync($"/spaces/{spaceUuid}/members", new { userCode = member.UserCode });

        var response = await owner.Client.DeleteAsync($"/spaces/{spaceUuid}/members/{member.UserUuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
        Assert.False(await db.SpaceMembers.AnyAsync(m => m.SpaceID == space.SpaceID && m.UserID == member.UserId));
    }

    /// <summary>Ruling G3: nguoi khong co CanManage khong duoc xoa NGUOI KHAC.</summary>
    [Fact]
    public async Task DeleteMember_non_manager_xoa_nguoi_khac_thi_bi_tu_choi()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var plainMember = await TestUser.CreateAsync(_fixture);
        var target = await TestUser.CreateAsync(_fixture);

        var create = await owner.Client.PostAsJsonAsync("/spaces/team", new { name = "Team Del2" });
        var created = await create.Content.ReadFromJsonAsync<OperationResultOf<SpaceResponse>>();
        var spaceUuid = created!.Data!.SpaceUuid;
        await owner.Client.PostAsJsonAsync($"/spaces/{spaceUuid}/members", new { userCode = plainMember.UserCode });
        await owner.Client.PostAsJsonAsync($"/spaces/{spaceUuid}/members", new { userCode = target.UserCode });

        var response = await plainMember.Client.DeleteAsync($"/spaces/{spaceUuid}/members/{target.UserUuid}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
        Assert.True(await db.SpaceMembers.AnyAsync(m => m.SpaceID == space.SpaceID && m.UserID == target.UserId));
    }

    /// <summary>Ruling G3: tu xoa chinh minh ("roi team") luon duoc phep, du khong co CanManage.</summary>
    [Fact]
    public async Task DeleteMember_tu_xoa_chinh_minh_luon_thanh_cong_du_khong_phai_manager()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var plainMember = await TestUser.CreateAsync(_fixture);

        var create = await owner.Client.PostAsJsonAsync("/spaces/team", new { name = "Team Leave" });
        var created = await create.Content.ReadFromJsonAsync<OperationResultOf<SpaceResponse>>();
        var spaceUuid = created!.Data!.SpaceUuid;
        await owner.Client.PostAsJsonAsync($"/spaces/{spaceUuid}/members", new { userCode = plainMember.UserCode });

        var response = await plainMember.Client.DeleteAsync($"/spaces/{spaceUuid}/members/{plainMember.UserUuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
        Assert.False(await db.SpaceMembers.AnyAsync(m => m.SpaceID == space.SpaceID && m.UserID == plainMember.UserId));
    }

    /// <summary>
    /// Final review finding 1: Owner duy nhat cua team KHONG duoc tu roi (leave) - lam vay se
    /// mo coi team vinh vien (khong con ai co quyen quan ly). 409 Conflict vi day la xung dot
    /// voi trang thai hien tai, khong phai thieu quyen.
    /// </summary>
    [Fact]
    public async Task DeleteMember_owner_duy_nhat_tu_roi_thi_bi_tu_choi_Conflict()
    {
        var owner = await TestUser.CreateAsync(_fixture);

        var create = await owner.Client.PostAsJsonAsync("/spaces/team", new { name = "Team SoloOwner" });
        var created = await create.Content.ReadFromJsonAsync<OperationResultOf<SpaceResponse>>();
        var spaceUuid = created!.Data!.SpaceUuid;

        var response = await owner.Client.DeleteAsync($"/spaces/{spaceUuid}/members/{owner.UserUuid}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
        Assert.True(await db.SpaceMembers.AnyAsync(m => m.SpaceID == space.SpaceID && m.UserID == owner.UserId));
    }

    /// <summary>
    /// Final review finding 1: khi co 2 Owner, mot Owner van duoc xoa Owner (hoac Admin) kia -
    /// khong bi chan boi rule "chi Owner moi xoa duoc Owner/Admin", vi chinh caller la Owner.
    /// </summary>
    [Fact]
    public async Task DeleteMember_Owner_xoa_duoc_Owner_khac_khi_con_nhieu_hon_1_Owner()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var secondOwner = await TestUser.CreateAsync(_fixture);

        var create = await owner.Client.PostAsJsonAsync("/spaces/team", new { name = "Team TwoOwners" });
        var created = await create.Content.ReadFromJsonAsync<OperationResultOf<SpaceResponse>>();
        var spaceUuid = created!.Data!.SpaceUuid;
        await owner.Client.PostAsJsonAsync($"/spaces/{spaceUuid}/members", new { userCode = secondOwner.UserCode });

        // Khong co endpoint doi vai tro trong pham vi task nay - nang thang len Owner qua DB.
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var membership = await db.SpaceMembers.FirstAsync(m => m.UserID == secondOwner.UserId);
            membership.MemberRole = SpaceMemberRole.Owner;
            await db.SaveChangesAsync();
        }

        var response = await owner.Client.DeleteAsync($"/spaces/{spaceUuid}/members/{secondOwner.UserUuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope2 = _fixture.CreateScope();
        var db2 = ApiFixture.Db(scope2);
        var space = await db2.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
        Assert.False(await db2.SpaceMembers.AnyAsync(m => m.SpaceID == space.SpaceID && m.UserID == secondOwner.UserId));
    }

    /// <summary>
    /// Final review finding 1: Owner van duoc xoa mot Admin (khac voi Owner khac, nhung cung
    /// nam trong nhom "Owner/Admin" bi rang buoc boi rule).
    /// </summary>
    [Fact]
    public async Task DeleteMember_Owner_xoa_duoc_Admin()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var admin = await TestUser.CreateAsync(_fixture);

        var create = await owner.Client.PostAsJsonAsync("/spaces/team", new { name = "Team OwnerRemovesAdmin" });
        var created = await create.Content.ReadFromJsonAsync<OperationResultOf<SpaceResponse>>();
        var spaceUuid = created!.Data!.SpaceUuid;
        await owner.Client.PostAsJsonAsync($"/spaces/{spaceUuid}/members", new { userCode = admin.UserCode });

        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var membership = await db.SpaceMembers.FirstAsync(m => m.UserID == admin.UserId);
            membership.MemberRole = SpaceMemberRole.Admin;
            await db.SaveChangesAsync();
        }

        var response = await owner.Client.DeleteAsync($"/spaces/{spaceUuid}/members/{admin.UserUuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Final review finding 1: Admin co CanManage nhung KHONG duoc xoa Owner - chi Owner moi co
    /// quyen nay (chan Admin chiem doat team bang cach xoa het Owner). 403 Forbidden vi day la
    /// thieu quyen, khac voi truong hop last-owner (409 Conflict).
    /// </summary>
    [Fact]
    public async Task DeleteMember_Admin_xoa_Owner_thi_bi_tu_choi_Forbidden()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var admin = await TestUser.CreateAsync(_fixture);

        var create = await owner.Client.PostAsJsonAsync("/spaces/team", new { name = "Team AdminVsOwner" });
        var created = await create.Content.ReadFromJsonAsync<OperationResultOf<SpaceResponse>>();
        var spaceUuid = created!.Data!.SpaceUuid;
        await owner.Client.PostAsJsonAsync($"/spaces/{spaceUuid}/members", new { userCode = admin.UserCode });

        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var membership = await db.SpaceMembers.FirstAsync(m => m.UserID == admin.UserId);
            membership.MemberRole = SpaceMemberRole.Admin;
            await db.SaveChangesAsync();
        }

        var response = await admin.Client.DeleteAsync($"/spaces/{spaceUuid}/members/{owner.UserUuid}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope2 = _fixture.CreateScope();
        var db2 = ApiFixture.Db(scope2);
        var space = await db2.Spaces.FirstAsync(s => s.SpaceUUID == spaceUuid);
        Assert.True(await db2.SpaceMembers.AnyAsync(m => m.SpaceID == space.SpaceID && m.UserID == owner.UserId));
    }
}
