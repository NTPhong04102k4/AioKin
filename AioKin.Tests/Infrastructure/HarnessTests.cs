using AioKin.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Infrastructure;

[Collection(ApiCollection.Name)]
public class HarnessTests
{
    private readonly ApiFixture _fixture;

    public HarnessTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task App_khoi_dong_thi_migration_va_seed_da_chay()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var roleNames = await db.Roles.Select(r => r.RoleName).ToListAsync();

        Assert.Contains(Roles.CUSTOMER, roleNames);
        Assert.Contains(Roles.SUPERADMIN, roleNames);
    }
}
