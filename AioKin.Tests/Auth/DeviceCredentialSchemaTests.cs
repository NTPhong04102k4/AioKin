using AioKin.Data.Entities.Security;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AioKin.Tests.Auth;

[Collection(ApiCollection.Name)]
public class DeviceCredentialSchemaTests
{
    private readonly ApiFixture _fixture;

    public DeviceCredentialSchemaTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Mot_user_khong_the_co_hai_credential_cung_deviceId()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.DeviceCredentials.Add(new DeviceCredential { UserID = user.UserID, DeviceId = "device-1", PublicKey = "key-a" });
        await db.SaveChangesAsync();

        db.DeviceCredentials.Add(new DeviceCredential { UserID = user.UserID, DeviceId = "device-1", PublicKey = "key-b" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
