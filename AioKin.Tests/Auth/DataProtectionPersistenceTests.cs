using AioKin.Data;
using AioKin.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AioKin.Tests.Auth;

[Collection(ApiCollection.Name)]
public class DataProtectionPersistenceTests
{
    private readonly ApiFixture _fixture;

    public DataProtectionPersistenceTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Khoa_DataProtection_duoc_luu_vao_Postgres_va_giai_ma_duoc_giua_cac_scope()
    {
        using var scope1 = _fixture.CreateScope();
        var dataProtectionProvider1 = scope1.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var protector1 = dataProtectionProvider1.CreateProtector("SessionSecurityTestPurpose");

        const string originalSecret = "user-login-session-secret-payload";
        var protectedData = protector1.Protect(originalSecret);

        Assert.NotEmpty(protectedData);
        Assert.NotEqual(originalSecret, protectedData);

        // Kiem tra bang security.data_protection_keys da co key duoc ghi xuong Postgres
        var db = ApiFixture.Db(scope1);
        var keys = await db.DataProtectionKeys.ToListAsync();
        Assert.NotEmpty(keys);
        Assert.All(keys, k =>
        {
            Assert.False(string.IsNullOrWhiteSpace(k.Xml));
        });

        // Mo phong container restart / instance khac doc tu DB:
        // Provider moi doc key tu database va giai ma thanh cong
        using var scope2 = _fixture.CreateScope();
        var dataProtectionProvider2 = scope2.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var protector2 = dataProtectionProvider2.CreateProtector("SessionSecurityTestPurpose");

        var unprotectedData = protector2.Unprotect(protectedData);
        Assert.Equal(originalSecret, unprotectedData);
    }
}
