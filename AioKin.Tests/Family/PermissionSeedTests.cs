using AioKin.Common;
using AioKin.Data.Entities.Core;
using AioKin.Data.Entities.Family;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Family;

[Collection(ApiCollection.Name)]
public class PermissionSeedTests
{
    private readonly ApiFixture _fixture;

    public PermissionSeedTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Rule_cua_Customer_co_nhac_den_Family_va_FamilyMember()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var rules = await db.Roles
            .Where(r => r.RoleName == Roles.CUSTOMER)
            .Select(r => r.Permissions)
            .SingleAsync();

        // Dung ten day du cho Family: namespace cua file test nay trung ten voi type
        // (AioKin.Tests.Family vs AioKin.Data.Entities.Family.Family), nen "Family" tran
        // se bi trinh bien dich hieu la namespace chi minh chu khong phai type.
        Assert.Contains(AioKin.Data.Entities.Family.Family.SubjectType, rules);
        Assert.Contains(FamilyMember.SubjectType, rules);
        Assert.Contains(FamilyInvite.SubjectType, rules);
    }

    [Fact]
    public async Task Rule_cu_van_giu_nguyen_thu_tu()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var rules = await db.Roles
            .Where(r => r.RoleName == Roles.CUSTOMER)
            .Select(r => r.Permissions)
            .SingleAsync();

        // Thu tu rule la ngu nghia: "doc duoc Kham pha" phai dung TRUOC "khong sua duoc
        // Kham pha", neu khong luat cam bien mat. Test nay chan moi lan sap xep lai.
        var readDiscovery = rules.IndexOf($$"""{"action":"read","subject":"{{DiscoveryItem.SubjectType}}"}""", StringComparison.Ordinal);
        var cannotEditDiscovery = rules.IndexOf("\"inverted\":true", StringComparison.Ordinal);

        Assert.True(readDiscovery >= 0, "Rule doc Kham pha da bien mat.");
        Assert.True(cannotEditDiscovery > readDiscovery, "Rule cam sua phai dung sau rule cho doc.");
    }
}
