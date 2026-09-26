using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AioKin.Common;
using AioKin.Models.InputModel.Auth.Admin;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Auth;

[Collection(ApiCollection.Name)]
public class AccessTokenAuthenticationTests
{
    private readonly ApiFixture _fixture;

    public AccessTokenAuthenticationTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Khong_co_Authorization_header_thi_tra_401()
    {
        var client = _fixture.CreateClient();

        var response = await client.GetAsync("/account/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bearer_token_gia_thi_tra_401_khong_500()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "khong-ton-tai-trong-redis");

        var response = await client.GetAsync("/account/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Access_token_hop_le_thi_qua_duoc_endpoint_can_Authorize()
    {
        var testUser = await TestUser.CreateAsync(_fixture);

        var response = await testUser.Client.GetAsync("/account/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Staff va Customer mang claim khac nhau (StaffId vs UserCode/UserUuid) — AdminAuthController
    /// dua vao GetStaffId() de biet ai la nguoi tao. Test nay di het duong: cap token Staff qua
    /// handler moi, goi mot route [Authorize(Roles = SuperAdmin)], roi kiem tra hieu ung quan sat
    /// duoc (CreatedBy trong database) de chac chan GetStaffId() doc dung tu claim cua token opaque
    /// chu khong chi la "khong bi 401/403".
    /// </summary>
    [Fact]
    public async Task Staff_token_hop_le_thi_qua_duoc_route_SuperAdmin_va_GetStaffId_doc_dung()
    {
        var admin = await TestStaff.CreateAsync(_fixture, Roles.SUPERADMIN);

        using var setupScope = _fixture.CreateScope();
        var setupDb = ApiFixture.Db(setupScope);
        var staffRole = await setupDb.Roles.FirstAsync(r => r.RoleName == Roles.STAFF);
        var location = await setupDb.Locations.OrderBy(l => l.LocationID).FirstAsync();

        var newUsername = $"newstaff{Guid.NewGuid():N}"[..20];
        var request = new CreateStaffRequest
        {
            Username = newUsername,
            Email = $"{Guid.NewGuid():N}@test.local",
            Password = "Test@12345",
            FullName = "Nhan Vien Moi",
            Phone = "0900000000",
            LocationID = location.LocationID,
            RoleID = staffRole.RoleID
        };

        var response = await admin.Client.PostAsJsonAsync("/auth/admin/staff", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var verifyScope = _fixture.CreateScope();
        var verifyDb = ApiFixture.Db(verifyScope);
        var createdStaff = await verifyDb.Staffs.FirstAsync(s => s.Username == newUsername);

        // CreatedBy den tu User.GetStaffId() ben trong AdminAuthController.CreateStaff — neu
        // claim StaffId khong duoc handler dien dung, gia tri nay se la 0 (hoac request se
        // 401 truoc do vi callerStaffId is null) chu khong the trung StaffId cua caller.
        Assert.Equal(admin.StaffId, createdStaff.CreatedBy);
    }

    /// <summary>
    /// Customer khong duoc vao route chi danh cho Admin/SuperAdmin — bao dam [Authorize(Roles = ...)]
    /// van tu choi dung role bi thieu, khong chi chap nhan bat ky token hop le nao.
    /// </summary>
    [Fact]
    public async Task Customer_token_bi_tu_choi_403_tren_route_chi_danh_cho_Admin()
    {
        var customer = await TestUser.CreateAsync(_fixture);

        var response = await customer.Client.GetAsync("/admin/staff");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
