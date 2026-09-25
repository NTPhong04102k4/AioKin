using AioKin.Common;
using AioKin.Data.Entities.Core;
using AioKin.Data.Entities.Family;
using AioKin.Data.Entities.Security;
using AioKin.Data.Entities.Vault;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AioKin.Data;

/// <summary>
/// Dua database ve trang thai toi thieu de luong auth chay duoc: bon role, mot location
/// mac dinh, va tai khoan SuperAdmin dau tien. Khong co nhung ban ghi nay thi
/// <c>POST /auth/admin/staff/create</c> khong bao gio goi duoc (can RoleID + LocationID
/// hop le) va cung khong ai dang nhap duoc de goi no — bai toan con ga qua trung.
///
/// Kem theo la bo rule phan quyen cho tung role va vai the Kham pha mau. Lich trinh thi
/// khong seed: no thuoc ve tung nguoi dung cu the, ma luc seed chua co nguoi dung nao.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AioKinDbContext db, IConfiguration config, ILogger logger)
    {
        await SeedRolesAsync(db, logger);
        await SeedRolePermissionsAsync(db, logger);
        await SeedDefaultLocationAsync(db, logger);
        await SeedSuperAdminAsync(db, config, logger);
        await SeedDiscoveryItemsAsync(db, logger);
        await BackfillFamilySpacesAsync(db, logger);
    }

    private static async Task SeedRolesAsync(AioKinDbContext db, ILogger logger)
    {
        var wanted = new (string Name, string Description)[]
        {
            (Roles.SUPERADMIN, "Toan quyen he thong. Chi ton tai duy nhat mot tai khoan."),
            (Roles.ADMIN, "Quan tri nguoi dung va nhan vien."),
            (Roles.STAFF, "Nhan vien nghiep vu."),
            (Roles.CUSTOMER, "Khach hang — dung cho phan quyen, khong gan vao bang Staff.")
        };

        var existing = await db.Roles.Select(r => r.RoleName).ToListAsync();
        var missing = wanted.Where(w => !existing.Contains(w.Name, StringComparer.Ordinal)).ToList();
        if (missing.Count == 0)
            return;

        db.Roles.AddRange(missing.Select(m => new Role { RoleName = m.Name, Description = m.Description }));
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} role(s): {Roles}", missing.Count, string.Join(", ", missing.Select(m => m.Name)));
    }

    /// <summary>
    /// Bo rule CASL mac dinh cho tung role.
    ///
    /// Role dang de rong (<c>[]</c>): gan nguyen van chuoi rules — bootstrap lan dau, giu
    /// dinh dang da viet tay ben duoi.
    ///
    /// Role da co rule tu truoc (database that, hoac lan seed truoc do): CHI bo sung cap
    /// SUBJECT+ACTION con thieu (xem <see cref="AppendMissingRules"/>), khong ghi de rule da
    /// co — bo rule la thu duoc sua bang tay tren database, va mot seeder ghi de moi lan
    /// khoi dong se lang le xoa cong sua do. Neu chi seed-khi-rong (nhu ban cu), mot database
    /// that/da migrate se khong bao gio nhan duoc rule moi them vao "wanted" o duoi (vi du
    /// Space/Prompt/Category/Tag) sau lan bootstrap dau tien — day la ly do can bo sung logic
    /// nay (Ruling D2, xem progress.md).
    ///
    /// THU TU PHAN TU LA NGU NGHIA: rule dung sau thang rule dung truoc. Chuoi duoi day
    /// duoc luu nguyen van va phat ra nguyen thu tu cho lan bootstrap dau — dao dong la doi
    /// luat. Rule bo sung sau nay luon duoc APPEND vao cuoi, khong bao gio chen giua.
    /// </summary>
    private static async Task SeedRolePermissionsAsync(AioKinDbContext db, ILogger logger)
    {
        // manage/all la dai dien cho moi hanh dong tren moi loai doi tuong.
        const string superAdminRules = """[{"action":"manage","subject":"all"}]""";

        const string adminRules = """[{"action":"manage","subject":"all"}]""";

        // Staff doc duoc moi thu nhung khong sua gi — khop dung voi cac endpoint /admin ma
        // role nay goi duoc: toan la GET.
        const string staffRules = """[{"action":"read","subject":"all"}]""";

        // Rule thu hai PHAI dung sau rule thu nhat: "doc duoc Kham pha, nhung khong sua".
        // Dao lai thi luat cam bien mat ma khong bao loi o dau ca.
        //
        // ScheduleItem khong kem dieu kien "cua chinh minh": endpoint /todos da gioi han
        // theo token roi, va DTO ben app khong phat userId ra nen mot dieu kien
        // {"userId": ...} se khong bao gio so khop duoc.
        // 4 dong cuoi (Space/Prompt/Category/Tag) dung "manage" rong hon cach Family lam
        // ("read"/"create" rieng le): pham vi truy cap thuc su da bi gioi han o tang service
        // boi ISpaceContext (thanh vien space + CanManage/author check), giong cach
        // FamilyMember.SubjectType chi can rule "read" don gian vi IFamilyContext moi la
        // cong gac that su. CASL o day chi xac nhan role Customer duoc dung toi cac loai
        // subject nay.
        const string customerRules = $$"""
            [
              {"action":"read","subject":"{{DiscoveryItem.SubjectType}}"},
              {"action":["create","update","delete"],"subject":"{{DiscoveryItem.SubjectType}}","inverted":true,"reason":"Noi dung Kham pha do ban bien tap quan ly."},
              {"action":"manage","subject":"{{ScheduleItem.SubjectType}}"},
              {"action":["read","create"],"subject":"{{Family.SubjectType}}"},
              {"action":["update","delete"],"subject":"{{Family.SubjectType}}","inverted":true,"reason":"Chi chu ho moi sua duoc thong tin gia dinh."},
              {"action":"read","subject":"{{FamilyMember.SubjectType}}"},
              {"action":["read","create"],"subject":"{{FamilyInvite.SubjectType}}"},
              {"action":"manage","subject":"{{Space.SubjectType}}"},
              {"action":"manage","subject":"{{Prompt.SubjectType}}"},
              {"action":"manage","subject":"{{Category.SubjectType}}"},
              {"action":"manage","subject":"{{Tag.SubjectType}}"}
            ]
            """;

        var wanted = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Roles.SUPERADMIN] = superAdminRules,
            [Roles.ADMIN] = adminRules,
            [Roles.STAFF] = staffRules,
            [Roles.CUSTOMER] = customerRules
        };

        var roles = await db.Roles.ToListAsync();
        var updated = new List<string>();

        foreach (var role in roles)
        {
            if (!wanted.TryGetValue(role.RoleName, out var rules))
                continue;

            // Rong hoac "[]" = chua ai dat rule — bootstrap lan dau, gan nguyen van.
            if (string.IsNullOrWhiteSpace(role.Permissions) || role.Permissions.Trim() == "[]")
            {
                role.Permissions = rules;
                updated.Add(role.RoleName);
                continue;
            }

            // Da co rule: chi bo sung cap SUBJECT+ACTION con thieu so voi "wanted", khong
            // dung lai neu khong thieu gi ca (giu nguyen van chuoi da co, kho khong doc lai).
            if (AppendMissingRules(role, rules, logger))
                updated.Add(role.RoleName);
        }

        if (updated.Count == 0)
            return;

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded permission cho {Count} role: {Roles}", updated.Count, string.Join(", ", updated));
    }

    /// <summary>
    /// So sanh rule "wanted" voi rule da co cua 1 role theo chu ky (subject, inverted,
    /// tap hop action da sap xep) — bo qua khac biet o "reason" (chi la mo ta). Rule wanted
    /// nao chua co chu ky trung thi duoc DeepClone va APPEND vao cuoi mang JSON hien co.
    /// Tra ve false (khong dung SaveChanges) neu khong co gi thieu, de tranh ghi lai
    /// Permissions bang mot chuoi JSON reserialize khac byte voi ban goc khi khong can thiet.
    ///
    /// Rule CASL hop le nhung ngoai du doan cua RuleSignature (vi du "subject" la MANG
    /// thay vi 1 chuoi — CASL cho phep dieu nay; hoac Permissions khong con la JSON hop le
    /// nua vi ly do gi do) se lam JsonNode.Parse/AsArray/GetValue nem loi. Ban CU chi seed
    /// khi rong nen chua bao gio gap truong hop nay; ban MOI nay chu dong doc lai rule da
    /// co nen PHAI chiu duoc hinh dang la — bat loi, ghi Warning, va bo qua BO SUNG cho
    /// dung role do (giu nguyen Permissions cu) thay vi lam SeedAsync nem loi va keo sap
    /// ca ung dung luc khoi dong.
    /// </summary>
    private static bool AppendMissingRules(Role role, string wantedRulesJson, ILogger logger)
    {
        try
        {
            var existingArray = JsonNode.Parse(role.Permissions!)?.AsArray();
            var wantedArray = JsonNode.Parse(wantedRulesJson)?.AsArray();
            if (existingArray is null || wantedArray is null)
                return false;

            var existingSignatures = existingArray
                .Where(n => n is not null)
                .Select(n => RuleSignature(n!))
                .ToHashSet(StringComparer.Ordinal);

            var missing = wantedArray
                .Where(n => n is not null && !existingSignatures.Contains(RuleSignature(n!)))
                .ToList();

            if (missing.Count == 0)
                return false;

            foreach (var rule in missing)
                existingArray.Add(rule!.DeepClone());

            role.Permissions = existingArray.ToJsonString();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            logger.LogWarning(ex,
                "Bo qua bo sung permission cho role {RoleName}: rule hien co (hoac rule wanted) co hinh dang CASL ngoai du doan cua bo so sanh (vi du subject dang mang).",
                role.RoleName);
            return false;
        }
    }

    /// <summary>Chu ky nhan dang 1 rule CASL: subject + co inverted khong + tap action da sap xep.</summary>
    private static string RuleSignature(JsonNode rule)
    {
        var obj = rule.AsObject();
        var subject = obj["subject"]?.GetValue<string>() ?? string.Empty;
        var inverted = obj["inverted"] is JsonValue invertedNode && invertedNode.GetValue<bool>();

        IEnumerable<string> actions = obj["action"] switch
        {
            JsonArray arr => arr.Where(a => a is not null).Select(a => a!.GetValue<string>()),
            JsonValue val => new[] { val.GetValue<string>() },
            _ => Array.Empty<string>()
        };

        return $"{subject}|{inverted}|{string.Join(",", actions.OrderBy(a => a, StringComparer.Ordinal))}";
    }

    private static async Task SeedDefaultLocationAsync(AioKinDbContext db, ILogger logger)
    {
        if (await db.Locations.AnyAsync())
            return;

        db.Locations.Add(new Location
        {
            LocationCode = "HQ",
            LocationName = "Head Office",
            LocationType = "Office",
            IsActive = true
        });
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded default location HQ.");
    }

    /// <summary>
    /// Tao SuperAdmin dau tien tu <c>Auth:SuperAdmin:*</c>. Khong co mat khau trong config
    /// thi bo qua thay vi dat mat khau mac dinh — mot mat khau mac dinh trong source code
    /// la tai khoan quan tri cong khai cho bat ky ai doc duoc repo.
    /// </summary>
    private static async Task SeedSuperAdminAsync(AioKinDbContext db, IConfiguration config, ILogger logger)
    {
        var superRole = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == Roles.SUPERADMIN);
        if (superRole is null)
            return;

        if (await db.Staffs.AnyAsync(s => s.RoleID == superRole.RoleID))
            return;

        var username = config["Auth:SuperAdmin:Username"];
        var email = config["Auth:SuperAdmin:Email"];
        var password = config["Auth:SuperAdmin:Password"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Chua co SuperAdmin va cung chua cau hinh Auth:SuperAdmin:{{Username,Email,Password}} — bo qua seed. "
                + "Dat ba gia tri nay (user-secrets hoac bien moi truong) roi chay lai de tao tai khoan quan tri dau tien.");
            return;
        }

        var location = await db.Locations.OrderBy(l => l.LocationID).FirstAsync();
        PasswordHelper.CreatePasswordHash(password, out var hash, out var salt);

        db.Staffs.Add(new Staff
        {
            Username = username,
            Email = email,
            FullName = config["Auth:SuperAdmin:FullName"] ?? "Super Administrator",
            PasswordHash = hash,
            PasswordSalt = salt,
            LocationID = location.LocationID,
            RoleID = superRole.RoleID,
            IsActive = true,
            CreatedBy = 0,
            StaffCode = Utils.HashTo20Chars($"STF-{location.LocationName}-L:{location.LocationID}-R:{superRole.RoleID}-U:{username}")
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded SuperAdmin account {Username}.", username);
    }

    /// <summary>
    /// Vai the Kham pha mau, de man hinh dau tien cua app khong trong tron.
    ///
    /// <c>AuthorUserID</c> KHONG phai so ngau nhien: ban DTO hien tai ben app van suy ra
    /// danh muc bang <c>userId % 3</c> theo thu tu enum TECHNOLOGY / HEALTH / LIFE. Nen moi
    /// AuthorUserID duoi day duoc chon sao cho ket qua suy ra do trung voi <c>Category</c>
    /// that. Doi mot trong hai ma quen cai kia thi app hien sai danh muc trong khi API van
    /// tra dung — mot lech pha khong loi, rat kho truy.
    /// </summary>
    private static async Task SeedDiscoveryItemsAsync(AioKinDbContext db, ILogger logger)
    {
        if (await db.DiscoveryItems.AnyAsync())
            return;

        var seeds = new (int AuthorUserId, string Category, string Title, string Body)[]
        {
            (3, "TECHNOLOGY", "Coroutine trong Android: bat dau tu dau",
             "Coroutine khong phai thread. No la mot cach viet code bat dong bo trong do trinh bien dich "
             + "chia ham cua ban thanh cac doan co the tam dung roi chay tiep. Vi vay mot nghin coroutine "
             + "van vua trong vai thread that. Diem quan trong khi moi bat dau la scope: coroutine song "
             + "trong mot scope, va scope bi huy thi moi coroutine ben trong cung dung theo. Do la ly do "
             + "viewModelScope ton tai - no huy dung luc ViewModel bi don di, nen khong con request nao "
             + "tro ve mot man hinh da dong."),

            (7, "HEALTH", "Ngoi lam viec lau: cai gia that va cach bu lai",
             "Van de khong nam o tu the ngoi dep hay xau, ma o chuyen khong doi tu the. Co bap giu nguyen "
             + "mot do dai qua lau se ngung doi tin hieu, va con dau lung xuat hien nhieu gio sau do. Cach "
             + "bu re nhat la dat mot moc thoi gian: cu khoang bon muoi lam phut thi dung day mot lan, di "
             + "vai buoc, roi ngoi lai. Khong can bai tap nao ca - chi can doi tu the truoc khi co the kip "
             + "quen mat tu the cu."),

            (5, "LIFE", "Ghi chep de nho, hay de quen di cho yen",
             "Nhieu nguoi ghi chu vi so quen. Nhung cong dung lon hon cua mot ghi chu la cho phep ban quen "
             + "mot cach an toan. Mot viec dang nam trong dau se tu nhac lai deu dan va an mot phan su chu y "
             + "cua ban suot ca ngay. Viet no ra, dat vao mot cho ban chac chan se doc lai, roi thoi khong "
             + "nghi den nua - phan chu y do quay ve viec ban dang lam that."),

            (9, "TECHNOLOGY", "Vi sao API nen tra ve ma loi on dinh",
             "Mot thong bao loi bang tieng nguoi la de doc, nhung client khong the phan nhanh theo no: sua "
             + "mot dau cham la moi doan if o phia kia sai het. Ma loi on dinh giai quyet dung cho do - "
             + "client so khop voi ma, con cau chu thi tu do doi bat cu luc nao. Doi lai, ma loi tro thanh "
             + "mot phan cua hop dong API: da phat ra roi thi khong doi y nghia duoc nua."),

            (13, "HEALTH", "Giac ngu ngan trong ngay: hai muoi phut, khong hon",
             "Mot giac ngan hai muoi phut dung o giai doan ngu nong, nen tinh day thay tinh tao ngay. Ngu "
             + "qua nguong do thi co the roi vao giai doan ngu sau, va bi danh thuc giua chung se de lai "
             + "cam giac nang dau keo dai ca tieng. Neu thay minh luon can giac ngan that dai, van de thuong "
             + "khong nam o buoi trua ma nam o gio di ngu toi hom truoc."),

            (11, "LIFE", "Lam mot viec kho vao dau ngay",
             "Kha nang tap trung khong phan bo deu trong ngay. No nhieu nhat vao vai gio dau sau khi ban "
             + "tinh tao, roi giam dan theo so quyet dinh ban phai dua ra. Dat viec kho nhat vao dung khoang "
             + "do, va de nhung viec lap di lap lai cho buoi chieu. Doi thu tu hai loai viec nay khong ton "
             + "them phut nao, nhung thuong doi ca chat luong cua viec kho lan cam giac cua ca ngay.")
        };

        db.DiscoveryItems.AddRange(seeds.Select(item => new DiscoveryItem
        {
            AuthorUserID = item.AuthorUserId,
            Title = item.Title,
            Body = item.Body,
            Category = item.Category,
            // Cung cong thuc voi ban DTO dang tu suy ra (do dai chia 200, toi thieu 1), de
            // hai ben ra cung con so trong giai doan app chua doc field that.
            ReadingMinutes = Math.Max(1, item.Body.Length / 200),
            IsPublished = true
        }));

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} the Kham pha.", seeds.Length);
    }

    /// <summary>
    /// Gia dinh tao truoc plan nay chua co Space tuong ung — Prompt domain se khong thay
    /// gia dinh do neu khong backfill. Idempotent: chi tao cho family chua co space.
    /// </summary>
    private static async Task BackfillFamilySpacesAsync(AioKinDbContext db, ILogger logger)
    {
        var missing = await db.Families
            .Where(f => f.IsActive && !db.Spaces.Any(s => s.FamilyID == f.FamilyID))
            .ToListAsync();

        if (missing.Count == 0)
            return;

        db.Spaces.AddRange(missing.Select(f => new Space
        {
            SpaceType = SpaceType.Family,
            Name = f.Name,
            OwnerUserID = f.OwnerUserID,
            FamilyID = f.FamilyID
        }));

        await db.SaveChangesAsync();
        logger.LogInformation("Backfilled {Count} family space(s).", missing.Count);
    }
}
