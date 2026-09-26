using AioKin.Common;
using AioKin.Data;
using AioKin.Data.Entities.Core;
using AioKin.Data.Entities.Family;
using AioKin.Data.Entities.Vault;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// Ruling D2: DbSeeder.SeedRolePermissionsAsync KHONG duoc chi seed-khi-rong nua — mot
    /// database that da migrate va co san rule M0 (Family/FamilyMember/FamilyInvite) thi
    /// van phai duoc BO SUNG cac rule con thieu (o day dai dien boi DiscoveryItem/
    /// ScheduleItem, vi Space/Prompt/Category/Tag cua Task 5 chua ton tai luc Task 3 chay)
    /// ma khong duoc dung lai rule M0 da co, va khong duoc tao rule trung khi chay lai lan
    /// nua.
    /// </summary>
    [Fact]
    public async Task SeedAsync_chay_lai_tren_DB_da_co_rule_M0_thi_bo_sung_khong_trung()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PermissionSeedTests");

        var role = await db.Roles.SingleAsync(r => r.RoleName == Roles.CUSTOMER);

        // Roles.CUSTOMER la mot ban ghi DUY NHAT dung chung voi MOI test khac trong cung
        // ApiCollection (khong co reset DB giua cac test). Phai luu lai gia tri that de
        // KHOI PHUC trong finally — neu khong, test nay se de lai Permissions bi sua doi
        // vinh vien, va Rule_cu_van_giu_nguyen_thu_tu (hoac bat ky test nao khac doc lai
        // Permissions cua Customer) se sai ket qua tuy thuoc thu tu chay method trong lop.
        var originalPermissions = role.Permissions;

        try
        {
            // Gia lap mot database "cu": da tung chay seed truoc khi cac rule sau nay (o day
            // dai dien boi DiscoveryItem/ScheduleItem) duoc them vao "wanted" — chi con lai
            // bo rule M0 cua gia dinh.
            const string oldRules = $$"""
                [
                  {"action":["read","create"],"subject":"{{AioKin.Data.Entities.Family.Family.SubjectType}}"},
                  {"action":["update","delete"],"subject":"{{AioKin.Data.Entities.Family.Family.SubjectType}}","inverted":true,"reason":"Chi chu ho moi sua duoc thong tin gia dinh."},
                  {"action":"read","subject":"{{FamilyMember.SubjectType}}"},
                  {"action":["read","create"],"subject":"{{FamilyInvite.SubjectType}}"}
                ]
                """;

            role.Permissions = oldRules;
            await db.SaveChangesAsync();

            await DbSeeder.SeedAsync(db, config, logger);

            var afterFirstRerun = await db.Roles.Where(r => r.RoleID == role.RoleID).Select(r => r.Permissions).SingleAsync();

            // Rule M0 con nguyen, khong bi nhan doi.
            Assert.Equal(1, CountOccurrences(afterFirstRerun!, FamilyMember.SubjectType));
            Assert.Contains(FamilyInvite.SubjectType, afterFirstRerun);

            // Rule con thieu so voi "wanted" hien tai da duoc BO SUNG, khong can seed lai tu dau.
            Assert.Contains(DiscoveryItem.SubjectType, afterFirstRerun);
            Assert.Contains(ScheduleItem.SubjectType, afterFirstRerun);

            // Chay lai lan nua tren DB da day du: khong duoc them trung, chuoi phai binh on.
            await DbSeeder.SeedAsync(db, config, logger);
            var afterSecondRerun = await db.Roles.Where(r => r.RoleID == role.RoleID).Select(r => r.Permissions).SingleAsync();

            Assert.Equal(afterFirstRerun, afterSecondRerun);
            Assert.Equal(1, CountOccurrences(afterSecondRerun!, FamilyMember.SubjectType));

            // DiscoveryItem xuat hien dung 2 lan trong "wanted" (rule "read" + rule "khong sua
            // duoc" inverted) — dem lai lan nua sau khi rerun de chan viec bo sung bi nhan doi,
            // khong phai vi so lan "dung" la 1.
            Assert.Equal(2, CountOccurrences(afterSecondRerun!, DiscoveryItem.SubjectType));
        }
        finally
        {
            // Khoi phuc nguyen trang cho moi test khac trong cung collection, bat ke assert
            // o tren pass hay fail.
            role.Permissions = originalPermissions;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Task 5: dong Ruling D2 lai tren SUBJECT THAT (Space/Prompt/Category/Tag) — Task 3
    /// chi kiem duoc bang DiscoveryItem/ScheduleItem dai dien vi 4 entity nay chua ton tai
    /// luc do. Xac nhan ca 4 rule "manage" duoc seed dung nhu customerRules khai bao.
    /// </summary>
    [Fact]
    public async Task Rule_cua_Customer_co_nhac_den_Space_Prompt_Category_Tag()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var rules = await db.Roles
            .Where(r => r.RoleName == Roles.CUSTOMER)
            .Select(r => r.Permissions)
            .SingleAsync();

        Assert.Contains($$"""{"action":"manage","subject":"{{Space.SubjectType}}"}""", rules);
        Assert.Contains($$"""{"action":"manage","subject":"{{Prompt.SubjectType}}"}""", rules);
        Assert.Contains($$"""{"action":"manage","subject":"{{Category.SubjectType}}"}""", rules);
        Assert.Contains($$"""{"action":"manage","subject":"{{Tag.SubjectType}}"}""", rules);
    }

    /// <summary>
    /// Task 5, dong tiep Ruling D2: gia lap mot DB da migrate tu TRUOC khi Task 5 them
    /// Space/Prompt/Category/Tag vao "wanted" (chi con rule M0 cua gia dinh). Chay lai seed
    /// phai BO SUNG dung 4 rule that nay, khong trung khi chay lai lan nua.
    /// </summary>
    [Fact]
    public async Task SeedAsync_chay_lai_tren_DB_thieu_rule_Space_Prompt_Category_Tag_thi_bo_sung_khong_trung()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PermissionSeedTests");

        var role = await db.Roles.SingleAsync(r => r.RoleName == Roles.CUSTOMER);
        var originalPermissions = role.Permissions;

        try
        {
            // Ban "cu" truoc Task 5: khong co Space/Prompt/Category/Tag, cac rule con lai
            // (DiscoveryItem/ScheduleItem/Family...) da co san tu M0/Task 3.
            const string preTask5Rules = $$"""
                [
                  {"action":"read","subject":"{{DiscoveryItem.SubjectType}}"},
                  {"action":["create","update","delete"],"subject":"{{DiscoveryItem.SubjectType}}","inverted":true,"reason":"Noi dung Kham pha do ban bien tap quan ly."},
                  {"action":"manage","subject":"{{ScheduleItem.SubjectType}}"},
                  {"action":["read","create"],"subject":"{{AioKin.Data.Entities.Family.Family.SubjectType}}"},
                  {"action":["update","delete"],"subject":"{{AioKin.Data.Entities.Family.Family.SubjectType}}","inverted":true,"reason":"Chi chu ho moi sua duoc thong tin gia dinh."},
                  {"action":"read","subject":"{{FamilyMember.SubjectType}}"},
                  {"action":["read","create"],"subject":"{{FamilyInvite.SubjectType}}"}
                ]
                """;

            role.Permissions = preTask5Rules;
            await db.SaveChangesAsync();

            await DbSeeder.SeedAsync(db, config, logger);

            var afterFirstRerun = await db.Roles.Where(r => r.RoleID == role.RoleID).Select(r => r.Permissions).SingleAsync();

            // Rule cu con nguyen, khong bi nhan doi.
            Assert.Equal(1, CountOccurrences(afterFirstRerun!, FamilyMember.SubjectType));

            // Ca 4 rule that cua Task 5 da duoc BO SUNG.
            Assert.Contains(Space.SubjectType, afterFirstRerun);
            Assert.Contains(Prompt.SubjectType, afterFirstRerun);
            Assert.Contains(Category.SubjectType, afterFirstRerun);
            Assert.Contains(Tag.SubjectType, afterFirstRerun);
            Assert.Equal(1, CountOccurrences(afterFirstRerun!, Space.SubjectType));
            Assert.Equal(1, CountOccurrences(afterFirstRerun!, Prompt.SubjectType));
            Assert.Equal(1, CountOccurrences(afterFirstRerun!, Category.SubjectType));
            Assert.Equal(1, CountOccurrences(afterFirstRerun!, Tag.SubjectType));

            // Chay lai lan nua tren DB da day du: khong duoc them trung, chuoi phai binh on.
            await DbSeeder.SeedAsync(db, config, logger);
            var afterSecondRerun = await db.Roles.Where(r => r.RoleID == role.RoleID).Select(r => r.Permissions).SingleAsync();

            Assert.Equal(afterFirstRerun, afterSecondRerun);
            Assert.Equal(1, CountOccurrences(afterSecondRerun!, Space.SubjectType));
            Assert.Equal(1, CountOccurrences(afterSecondRerun!, Prompt.SubjectType));
            Assert.Equal(1, CountOccurrences(afterSecondRerun!, Category.SubjectType));
            Assert.Equal(1, CountOccurrences(afterSecondRerun!, Tag.SubjectType));
        }
        finally
        {
            role.Permissions = originalPermissions;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Ruling D2 (fix round 1): mot rule CASL hop le nhung ngoai du doan cua bo so sanh
    /// signature (o day: "subject" la MANG ["A","B"] thay vi 1 chuoi — CASL cho phep dieu
    /// nay) khong duoc lam SeedAsync nem loi va lam sap ung dung luc khoi dong. Ban cu chi
    /// bo qua bo sung cho role do, giu nguyen rule "la" thay vi crash.
    /// </summary>
    [Fact]
    public async Task SeedAsync_role_co_subject_dang_mang_thi_bo_qua_khong_nem_loi()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PermissionSeedTests");

        var role = await db.Roles.SingleAsync(r => r.RoleName == Roles.CUSTOMER);
        var originalPermissions = role.Permissions;

        try
        {
            const string weirdRules = """[{"action":"read","subject":["A","B"]}]""";
            role.Permissions = weirdRules;
            await db.SaveChangesAsync();

            var exception = await Record.ExceptionAsync(() => DbSeeder.SeedAsync(db, config, logger));

            Assert.Null(exception);

            var afterSeed = await db.Roles.Where(r => r.RoleID == role.RoleID).Select(r => r.Permissions).SingleAsync();

            // Bo qua hoan toan cho role nay: rule "la" van con nguyen, khong bi bo sung gi them.
            Assert.Equal(weirdRules, afterSeed);
        }
        finally
        {
            role.Permissions = originalPermissions;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Final review finding 3: JsonNode.Parse nem ArgumentException (khac voi JsonException)
    /// tren mot JSON object co KEY TRUNG NHAU — de xay ra khi Role.Permissions bi sua tay vi
    /// day chi la mot string thuong, khong co rang buoc JSON hop le o tang DB. Catch filter cu
    /// (JsonException or InvalidOperationException) khong bat duoc ArgumentException nen loi
    /// nay se vuot qua va lam sap ung dung ngay luc khoi dong — dung nguoc muc dich Ruling D2.
    /// </summary>
    [Fact]
    public async Task SeedAsync_role_co_json_key_trung_thi_bo_qua_khong_nem_loi()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PermissionSeedTests");

        var role = await db.Roles.SingleAsync(r => r.RoleName == Roles.CUSTOMER);
        var originalPermissions = role.Permissions;

        try
        {
            // "subject" xuat hien 2 lan trong cung 1 object -> key trung.
            const string duplicateKeyRules = """[{"action":"read","subject":"X","subject":"Y"}]""";
            role.Permissions = duplicateKeyRules;
            await db.SaveChangesAsync();

            var exception = await Record.ExceptionAsync(() => DbSeeder.SeedAsync(db, config, logger));

            Assert.Null(exception);

            var afterSeed = await db.Roles.Where(r => r.RoleID == role.RoleID).Select(r => r.Permissions).SingleAsync();

            // Bo qua hoan toan cho role nay: rule "la" van con nguyen, khong bi bo sung gi them.
            Assert.Equal(duplicateKeyRules, afterSeed);
        }
        finally
        {
            role.Permissions = originalPermissions;
            await db.SaveChangesAsync();
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
