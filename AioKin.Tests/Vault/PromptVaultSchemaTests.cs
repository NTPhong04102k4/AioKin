using AioKin.Data.Entities.Vault;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Vault;

[Collection(ApiCollection.Name)]
public class PromptVaultSchemaTests
{
    private readonly ApiFixture _fixture;

    public PromptVaultSchemaTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Mot_family_khong_the_gan_hai_space()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        db.Users.Add(user);
        var family = new AioKin.Data.Entities.Family.Family { Name = "Nha A", OwnerUserID = user.UserID };
        db.Families.Add(family);
        await db.SaveChangesAsync();

        db.Spaces.Add(new Space { SpaceType = SpaceType.Family, Name = "Nha A", OwnerUserID = user.UserID, FamilyID = family.FamilyID });
        await db.SaveChangesAsync();

        db.Spaces.Add(new Space { SpaceType = SpaceType.Family, Name = "Nha A (2)", OwnerUserID = user.UserID, FamilyID = family.FamilyID });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
