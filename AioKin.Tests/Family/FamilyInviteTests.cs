using AioKin.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AioKin.Tests.Family;

[Collection(ApiCollection.Name)]
public class FamilyInviteTests
{
    private readonly ApiFixture _fixture;

    public FamilyInviteTests(ApiFixture fixture) => _fixture = fixture;

    private static async Task<Guid> CreateFamilyAsync(TestUser user, string name)
    {
        var response = await user.Client.PostAsJsonAsync("/families", new { name });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("familyUuid").GetGuid();
    }

    private static async Task<string> CreateInviteAsync(TestUser user, Guid familyUuid, int maxUses = 5)
    {
        var response = await user.Client.PostAsJsonAsync(
            $"/families/{familyUuid}/invites", new { maxUses, expiresInHours = 24 });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Vao_nhom_bang_ma_hop_le_thi_thanh_Adult()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var joiner = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha moi");
        var code = await CreateInviteAsync(owner, familyUuid);

        var response = await joiner.Client.PostAsJsonAsync("/families/join", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mine = await joiner.Client.GetFromJsonAsync<JsonElement>("/families/me");
        var items = mine.GetProperty("data").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("Adult", items[0].GetProperty("myRole").GetString());
        Assert.Equal(2, items[0].GetProperty("memberCount").GetInt32());
    }

    [Fact]
    public async Task Nguoi_ngoai_khong_tao_duoc_ma_moi()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var outsider = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha kin");

        var response = await outsider.Client.PostAsJsonAsync(
            $"/families/{familyUuid}/invites", new { maxUses = 5, expiresInHours = 24 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NotAFamilyMember", body.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Ma_dung_het_so_lan_thi_khong_dung_duoc_nua()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var first = await TestUser.CreateAsync(_fixture);
        var second = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha mot suat");
        var code = await CreateInviteAsync(owner, familyUuid, maxUses: 1);

        Assert.Equal(HttpStatusCode.OK,
            (await first.Client.PostAsJsonAsync("/families/join", new { code })).StatusCode);

        var response = await second.Client.PostAsJsonAsync("/families/join", new { code });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Vao_lai_nhom_da_o_trong_do_thi_khong_ton_them_luot()
    {
        var owner = await TestUser.CreateAsync(_fixture);
        var joiner = await TestUser.CreateAsync(_fixture);
        var familyUuid = await CreateFamilyAsync(owner, "Nha vao hai lan");
        var code = await CreateInviteAsync(owner, familyUuid, maxUses: 2);

        await joiner.Client.PostAsJsonAsync("/families/join", new { code });
        var response = await joiner.Client.PostAsJsonAsync("/families/join", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mine = await joiner.Client.GetFromJsonAsync<JsonElement>("/families/me");
        Assert.Single(mine.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Ma_khong_ton_tai_thi_tra_404()
    {
        var user = await TestUser.CreateAsync(_fixture);

        var response = await user.Client.PostAsJsonAsync("/families/join", new { code = "ZZZZZZZZZZ" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
