using AioKin.Data.Entities.Security;
using AioKin.Services.Auth.Token;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;

namespace AioKin.Tests.Infrastructure;

/// <summary>
/// Mot nhan vien (Staff/Admin/SuperAdmin) da dang nhap, kem HttpClient da gan token.
///
/// Tao Staff thang trong database va cap token bang chinh IAccessTokenService cua app,
/// thay vi di qua /auth/admin/login that: dang nhap that can mat khau dung va khong phai
/// thu dang duoc kiem tra o day. Dung Role/Location co san (DbSeeder da seed bon role va
/// mot location HQ luc khoi dong app trong test).
/// </summary>
public sealed class TestStaff
{
    public required HttpClient Client { get; init; }
    public required int StaffId { get; init; }

    public static async Task<TestStaff> CreateAsync(ApiFixture fixture, string roleName)
    {
        using var scope = fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var role = await db.Roles.FirstAsync(r => r.RoleName == roleName);
        var location = await db.Locations.OrderBy(l => l.LocationID).FirstAsync();

        var staff = TestData.NewStaff(role.RoleID, location.LocationID);
        db.Staffs.Add(staff);
        await db.SaveChangesAsync();

        var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();
        var accessToken = await tokens.CreateForStaffAsync(staff, roleName, DeviceInfo.Unknown);

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return new TestStaff { Client = client, StaffId = staff.StaffID };
    }
}
