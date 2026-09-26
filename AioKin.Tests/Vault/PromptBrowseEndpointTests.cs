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
/// PromptsController: chi doc. Khong co test POST/PUT/DELETE o day vi controller khong co
/// action nao nhu vay — 4 entity Prompt/Category/Tag/PromptVariable chi ghi qua sync engine
/// (plan rieng). Bao gom Expo gap G5 (categories/tags) va cac test da bi thieu tu Review Focus
/// L31-32: family-vs-team isolation va Child-role read.
/// </summary>
[Collection(ApiCollection.Name)]
public class PromptBrowseEndpointTests
{
    private readonly ApiFixture _fixture;

    public PromptBrowseEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Liet_ke_prompt_trong_personal_space_cua_chinh_minh()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var mine = await testUser.Client.GetFromJsonAsync<OperationResultOf<List<SpaceResponse>>>("/spaces/me");
        var personalSpaceUuid = mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;

        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == personalSpaceUuid);
            var category = new Category { CategoryID = Guid.NewGuid(), SpaceID = space.SpaceID, Name = "Marketing" };
            db.Categories.Add(category);
            db.Prompts.Add(new Prompt
            {
                PromptID = Guid.NewGuid(),
                SpaceID = space.SpaceID,
                AuthorUserID = testUser.UserId,
                CategoryID = category.CategoryID,
                Title = "Caption skincare",
                Content = "Viet caption quang cao san pham skincare"
            });
            await db.SaveChangesAsync();
        }

        var response = await testUser.Client.GetAsync($"/prompts?spaceUuid={personalSpaceUuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<List<PromptSummaryResponse>>>();
        var item = Assert.Single(body!.Data!);
        Assert.Equal("Caption skincare", item.Title);
        // G5: CategoryId/CategoryName phai co tren dang danh sach.
        Assert.NotNull(item.CategoryId);
        Assert.Equal("Marketing", item.CategoryName);
    }

    [Fact]
    public async Task Xem_chi_tiet_prompt_tra_ve_day_du_category_tag_variable()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var mine = await testUser.Client.GetFromJsonAsync<OperationResultOf<List<SpaceResponse>>>("/spaces/me");
        var personalSpaceUuid = mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;

        Guid promptId;
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == personalSpaceUuid);
            var category = new Category { CategoryID = Guid.NewGuid(), SpaceID = space.SpaceID, Name = "Cham soc da" };
            var tag = new Tag { TagID = Guid.NewGuid(), SpaceID = space.SpaceID, Name = "skincare" };
            db.Categories.Add(category);
            db.Tags.Add(tag);

            promptId = Guid.NewGuid();
            db.Prompts.Add(new Prompt
            {
                PromptID = promptId,
                SpaceID = space.SpaceID,
                AuthorUserID = testUser.UserId,
                CategoryID = category.CategoryID,
                Title = "Caption skincare",
                Content = "Noi dung day du",
                Version = 3
            });
            db.PromptVariables.Add(new PromptVariable
            {
                VariableID = Guid.NewGuid(),
                PromptID = promptId,
                VarKey = "ten_san_pham",
                Label = "Ten san pham"
            });
            db.PromptTags.Add(new PromptTag { PromptID = promptId, TagID = tag.TagID });
            await db.SaveChangesAsync();
        }

        var response = await testUser.Client.GetAsync($"/prompts/{promptId}?spaceUuid={personalSpaceUuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<PromptDetailResponse>>();
        Assert.Equal("Caption skincare", body!.Data!.Title);
        Assert.Equal(3, body.Data.Version);
        // G5: CategoryId/CategoryName phai co tren dang chi tiet.
        Assert.NotNull(body.Data.CategoryId);
        Assert.Equal("Cham soc da", body.Data.CategoryName);
        Assert.Contains("skincare", body.Data.Tags);
        Assert.Contains(body.Data.Variables, v => v.VarKey == "ten_san_pham");
    }

    [Fact]
    public async Task Nguoi_ngoai_space_bi_tu_choi()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var stranger = await TestUser.CreateAsync(_fixture);

        var mine = await owner.Client.GetFromJsonAsync<OperationResultOf<List<SpaceResponse>>>("/spaces/me");
        var personalSpaceUuid = mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;

        var response = await stranger.Client.GetAsync($"/prompts?spaceUuid={personalSpaceUuid}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Review Focus L31-32: mot nguoi chi thuoc Team khong duoc doc prompt cua Family space
    /// (va nguoc lai) chi bang cach doan/dung lai spaceUuid — ISpaceContext phai kiem tra dung
    /// tung space cu the, khong phai "thuoc bat ky space nao".
    /// </summary>
    [Fact]
    public async Task Family_vs_Team_khong_doc_cheo_prompt_cua_nhau()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var teamOnlyUser = await TestUser.CreateAsync(_fixture);
        var familyOnlyUser = await TestUser.CreateAsync(_fixture);

        var createTeam = await owner.Client.PostAsJsonAsync("/spaces/team", new { name = "Team Isolation" });
        var teamCreated = await createTeam.Content.ReadFromJsonAsync<OperationResultOf<SpaceResponse>>();
        var teamSpaceUuid = teamCreated!.Data!.SpaceUuid;
        await owner.Client.PostAsJsonAsync($"/spaces/{teamSpaceUuid}/members", new { userCode = teamOnlyUser.UserCode });

        Guid familySpaceUuid;
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var ownerRow = await db.Users.FirstAsync(u => u.UserUUID == owner.UserUuid);
            var familyOnlyRow = await db.Users.FirstAsync(u => u.UserUUID == familyOnlyUser.UserUuid);

            var family = new FamilyDb { Name = "Nha Isolation Prompt", OwnerUserID = ownerRow.UserID };
            db.Families.Add(family);
            db.FamilyMembers.Add(new FamilyMember { FamilyID = family.FamilyID, UserID = ownerRow.UserID, MemberRole = FamilyMemberRole.Owner });
            db.FamilyMembers.Add(new FamilyMember { FamilyID = family.FamilyID, UserID = familyOnlyRow.UserID, MemberRole = FamilyMemberRole.Adult });

            var familySpace = new Space { SpaceType = SpaceType.Family, Name = family.Name, OwnerUserID = ownerRow.UserID, FamilyID = family.FamilyID };
            db.Spaces.Add(familySpace);
            await db.SaveChangesAsync();
            familySpaceUuid = familySpace.SpaceUUID;
        }

        // teamOnlyUser khong phai thanh vien family -> tu choi.
        var teamUserReadsFamily = await teamOnlyUser.Client.GetAsync($"/prompts?spaceUuid={familySpaceUuid}");
        Assert.Equal(HttpStatusCode.Forbidden, teamUserReadsFamily.StatusCode);

        // familyOnlyUser khong phai thanh vien team -> tu choi.
        var familyUserReadsTeam = await familyOnlyUser.Client.GetAsync($"/prompts?spaceUuid={teamSpaceUuid}");
        Assert.Equal(HttpStatusCode.Forbidden, familyUserReadsTeam.StatusCode);
    }

    /// <summary>
    /// Review Focus L31-32: Child trong gia dinh la thanh vien read-only, nhung browsing la
    /// hanh dong doc (khong phai quan ly) nen Child VAN doc duoc, khong can CanManage.
    /// </summary>
    [Fact]
    public async Task Child_trong_gia_dinh_van_doc_duoc_prompt_cua_family_space()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var child = await TestUser.CreateAsync(_fixture);

        Guid familySpaceUuid;
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var ownerRow = await db.Users.FirstAsync(u => u.UserUUID == owner.UserUuid);
            var childRow = await db.Users.FirstAsync(u => u.UserUUID == child.UserUuid);

            var family = new FamilyDb { Name = "Nha Co Con", OwnerUserID = ownerRow.UserID };
            db.Families.Add(family);
            db.FamilyMembers.Add(new FamilyMember { FamilyID = family.FamilyID, UserID = ownerRow.UserID, MemberRole = FamilyMemberRole.Owner });
            db.FamilyMembers.Add(new FamilyMember { FamilyID = family.FamilyID, UserID = childRow.UserID, MemberRole = FamilyMemberRole.Child });

            var familySpace = new Space { SpaceType = SpaceType.Family, Name = family.Name, OwnerUserID = ownerRow.UserID, FamilyID = family.FamilyID };
            db.Spaces.Add(familySpace);
            db.Prompts.Add(new Prompt
            {
                PromptID = Guid.NewGuid(),
                SpaceID = familySpace.SpaceID,
                AuthorUserID = ownerRow.UserID,
                Title = "Bai tap ve nha",
                Content = "Noi dung"
            });
            await db.SaveChangesAsync();
            familySpaceUuid = familySpace.SpaceUUID;
        }

        var response = await child.Client.GetAsync($"/prompts?spaceUuid={familySpaceUuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationResultOf<List<PromptSummaryResponse>>>();
        Assert.Single(body!.Data!);
    }

    /// <summary>Expo gap G5.</summary>
    [Fact]
    public async Task Danh_sach_category_va_tag_scoped_theo_space()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var mine = await testUser.Client.GetFromJsonAsync<OperationResultOf<List<SpaceResponse>>>("/spaces/me");
        var personalSpaceUuid = mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;

        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var space = await db.Spaces.FirstAsync(s => s.SpaceUUID == personalSpaceUuid);
            db.Categories.Add(new Category { CategoryID = Guid.NewGuid(), SpaceID = space.SpaceID, Name = "Ban hang" });
            db.Tags.Add(new Tag { TagID = Guid.NewGuid(), SpaceID = space.SpaceID, Name = "urgent" });
            await db.SaveChangesAsync();
        }

        var categoriesResponse = await testUser.Client.GetAsync($"/prompts/categories?spaceUuid={personalSpaceUuid}");
        Assert.Equal(HttpStatusCode.OK, categoriesResponse.StatusCode);
        var categoriesBody = await categoriesResponse.Content.ReadFromJsonAsync<OperationResultOf<List<CategoryResponse>>>();
        Assert.Contains(categoriesBody!.Data!, c => c.Name == "Ban hang");

        var tagsResponse = await testUser.Client.GetAsync($"/prompts/tags?spaceUuid={personalSpaceUuid}");
        Assert.Equal(HttpStatusCode.OK, tagsResponse.StatusCode);
        var tagsBody = await tagsResponse.Content.ReadFromJsonAsync<OperationResultOf<List<TagResponse>>>();
        Assert.Contains(tagsBody!.Data!, t => t.Name == "urgent");
    }

    /// <summary>
    /// Final review finding 2: mot thanh vien THAT cua team A khong duoc doc prompt cua team B
    /// bang cach truyen promptId that cua B kem spaceUuid cua CHINH minh (team A). Code da lam
    /// dung dieu nay (PromptBrowseService.GetAsync loc theo p.SpaceID == membership.SpaceID,
    /// tuc SpaceID cua space A) - test nay chi dong lai lo hong coverage tren tuyen phong thu
    /// IDOR quan trong nhat cua plan.
    /// </summary>
    [Fact]
    public async Task Thanh_vien_space_A_khong_doc_duoc_prompt_cua_space_B_bang_spaceUuid_cua_minh()
    {
        var ownerA = await TestUser.CreateAsync(_fixture);
        var memberA = await TestUser.CreateAsync(_fixture);
        var ownerB = await TestUser.CreateAsync(_fixture);

        var createA = await ownerA.Client.PostAsJsonAsync("/spaces/team", new { name = "Team A IDOR" });
        var createdA = await createA.Content.ReadFromJsonAsync<OperationResultOf<SpaceResponse>>();
        var spaceAUuid = createdA!.Data!.SpaceUuid;
        await ownerA.Client.PostAsJsonAsync($"/spaces/{spaceAUuid}/members", new { userCode = memberA.UserCode });

        var createB = await ownerB.Client.PostAsJsonAsync("/spaces/team", new { name = "Team B IDOR" });
        var createdB = await createB.Content.ReadFromJsonAsync<OperationResultOf<SpaceResponse>>();
        var spaceBUuid = createdB!.Data!.SpaceUuid;

        Guid promptIdFromB;
        using (var scope = _fixture.CreateScope())
        {
            var db = ApiFixture.Db(scope);
            var ownerBRow = await db.Users.FirstAsync(u => u.UserUUID == ownerB.UserUuid);
            var spaceB = await db.Spaces.FirstAsync(s => s.SpaceUUID == spaceBUuid);

            promptIdFromB = Guid.NewGuid();
            db.Prompts.Add(new Prompt
            {
                PromptID = promptIdFromB,
                SpaceID = spaceB.SpaceID,
                AuthorUserID = ownerBRow.UserID,
                Title = "Bi mat cua team B",
                Content = "Khong ai o team A duoc doc cai nay"
            });
            await db.SaveChangesAsync();
        }

        // memberA la thanh vien THAT cua team A - truyen promptId cua B kem spaceUuid cua A.
        var response = await memberA.Client.GetAsync($"/prompts/{promptIdFromB}?spaceUuid={spaceAUuid}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Expo gap G5: category/tag cua space khac khong duoc lo ra cho nguoi ngoai.</summary>
    [Fact]
    public async Task Categories_va_tags_tu_choi_nguoi_ngoai_space()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var stranger = await TestUser.CreateAsync(_fixture);

        var mine = await owner.Client.GetFromJsonAsync<OperationResultOf<List<SpaceResponse>>>("/spaces/me");
        var personalSpaceUuid = mine!.Data!.First(s => s.SpaceType == "Personal").SpaceUuid;

        var categoriesResponse = await stranger.Client.GetAsync($"/prompts/categories?spaceUuid={personalSpaceUuid}");
        Assert.Equal(HttpStatusCode.Forbidden, categoriesResponse.StatusCode);

        var tagsResponse = await stranger.Client.GetAsync($"/prompts/tags?spaceUuid={personalSpaceUuid}");
        Assert.Equal(HttpStatusCode.Forbidden, tagsResponse.StatusCode);
    }
}
