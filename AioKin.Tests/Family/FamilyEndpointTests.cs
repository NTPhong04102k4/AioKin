using AioKin.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AioKin.Tests.Family;

[Collection(ApiCollection.Name)]
public class FamilyEndpointTests
{
    private readonly ApiFixture _fixture;

    public FamilyEndpointTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Tao_gia_dinh_thi_nguoi_tao_thanh_Owner()
    {
        var user = await TestUser.CreateAsync(_fixture);

        var response = await user.Client.PostAsJsonAsync("/families", new { name = "Nha Phong" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());

        var data = body.GetProperty("data");
        Assert.Equal("Nha Phong", data.GetProperty("name").GetString());
        Assert.Equal("Owner", data.GetProperty("myRole").GetString());
        Assert.Equal(1, data.GetProperty("memberCount").GetInt32());
    }

    [Fact]
    public async Task Danh_sach_chi_tra_ve_gia_dinh_cua_chinh_minh()
    {
        var mine = await TestUser.CreateAsync(_fixture);
        var other = await TestUser.CreateAsync(_fixture);

        await mine.Client.PostAsJsonAsync("/families", new { name = "Nha cua toi" });
        await other.Client.PostAsJsonAsync("/families", new { name = "Nha nguoi khac" });

        var response = await mine.Client.GetAsync("/families/me");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal("Nha cua toi", items[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Ten_rong_thi_bi_tu_choi()
    {
        var user = await TestUser.CreateAsync(_fixture);

        var response = await user.Client.PostAsJsonAsync("/families", new { name = "  " });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Khong_co_token_thi_bi_tu_choi()
    {
        var anonymous = _fixture.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/families", new { name = "Nha la" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
