using AioKin.Data.Entities.Core;
using AioKin.Data.Entities.Family;
using AioKin.Data.Entities.Security;
using AioKin.Data.Entities.Vault;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Data;

/// <summary>
/// DbContext cho phan Security (auth). Ten bang/cot theo snake_case — cau hinh
/// <c>UseSnakeCaseNamingConvention()</c> trong Program.cs lo phan chuyen doi.
/// </summary>
public class AioKinDbContext(DbContextOptions<AioKinDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Staff> Staffs => Set<Staff>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<DiscoveryItem> DiscoveryItems => Set<DiscoveryItem>();
    public DbSet<ScheduleItem> ScheduleItems => Set<ScheduleItem>();
    public DbSet<Family> Families => Set<Family>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<FamilyInvite> FamilyInvites => Set<FamilyInvite>();
    public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<SpaceMember> SpaceMembers => Set<SpaceMember>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Prompt> Prompts => Set<Prompt>();
    public DbSet<PromptVariable> PromptVariables => Set<PromptVariable>();
    public DbSet<PromptTag> PromptTags => Set<PromptTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            // UserUUID la id cong khai, moi API tra cuu qua no — phai unique va co index.
            entity.HasIndex(u => u.UserUUID).IsUnique();
            entity.HasIndex(u => u.UserCode).IsUnique();
            entity.HasIndex(u => u.Username).IsUnique();

            // Email/Phone unique nhung cho phep nhieu NULL: tai khoan Facebook co the
            // khong tra email, va tai khoan dang ky bang email thi khong co phone.
            entity.HasIndex(u => u.Email).IsUnique().HasFilter("email IS NOT NULL");
            entity.HasIndex(u => u.Phone).IsUnique().HasFilter("phone IS NOT NULL");

            // Tra cuu khi dang nhap SSO — mot social id chi gan duoc mot tai khoan.
            entity.HasIndex(u => u.IDSocial).IsUnique().HasFilter("id_social IS NOT NULL");

            // FullName do Postgres tinh, khong bao gio ghi tu ung dung.
            //
            // Bieu thuc phai IMMUTABLE thi Postgres moi cho lam cot GENERATED. CONCAT_WS
            // va CONCAT chi la STABLE (chung phu thuoc ham xuat cua kieu du lieu), nen
            // dung chung se bi tu choi voi loi 42P17. COALESCE + toan tu || deu immutable:
            // COALESCE lo phan mot ve NULL khong nuot ca chuoi, NULLIF tra ve NULL thay vi
            // chuoi rong khi ca hai ve deu trong.
            entity.Property(u => u.FullName)
                .HasComputedColumnSql(
                    """NULLIF(TRIM(COALESCE("first_name", '') || ' ' || COALESCE("last_name", '')), '')""",
                    stored: true);
        });

        modelBuilder.Entity<Staff>(entity =>
        {
            entity.HasIndex(s => s.Username).IsUnique();
            entity.HasIndex(s => s.Email).IsUnique();
            entity.HasIndex(s => s.StaffCode).IsUnique();

            entity.HasOne(s => s.Role)
                .WithMany(r => r.Staffs)
                .HasForeignKey(s => s.RoleID)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(s => s.Location)
                .WithMany()
                .HasForeignKey(s => s.LocationID)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeviceCredential>(entity =>
        {
            // Mot thiet bi mot credential moi user — dang ky lai (doi key, cai lai app)
            // phai la UPDATE, khong phai insert them dong. Index unique nay da phu ca
            // truy van theo UserID (la cot dau cua composite index) nen khong can them
            // mot index rieng cho UserID.
            entity.HasIndex(c => new { c.UserID, c.DeviceId }).IsUnique();

            entity.HasOne<AioKin.Data.Entities.Security.User>()
                .WithMany()
                .HasForeignKey(c => c.UserID)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Role>(entity => entity.HasIndex(r => r.RoleName).IsUnique());

        modelBuilder.Entity<Location>(entity => entity.HasIndex(l => l.LocationCode).IsUnique());

        modelBuilder.Entity<DiscoveryItem>(entity =>
        {
            // Truy van duy nhat cua man Kham pha la "bai da publish, moi nhat truoc" — index
            // phu dung thu tu do de Postgres khong phai sort lai ca bang sau moi lan loc.
            entity.HasIndex(d => new { d.IsPublished, d.CreatedDate });

            // Danh muc duoc loc o tang UI, nhung khi so bai lon len thi bo loc se chuyen
            // xuong duoi nay; co san index thi luc do khong phai them migration.
            entity.HasIndex(d => d.Category);
        });

        modelBuilder.Entity<ScheduleItem>(entity =>
        {
            // Moi truy van deu la "lich cua mot nguoi, sap theo gio bat dau" — index ghep
            // theo dung cap do phuc vu ca phan loc lan phan sap xep trong mot lan quet.
            entity.HasIndex(s => new { s.UserID, s.StartAt });

            // Cascade chu khong Restrict: lich trinh khong ton tai doc lap voi chu cua no.
            // Restrict o day nghia la xoa mot tai khoan se that bai voi loi khoa ngoai, con
            // du lieu rieng tu cua nguoi da roi di thi nam lai trong bang.
            entity.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserID)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Family>(entity =>
        {
            entity.HasIndex(f => f.FamilyUUID).IsUnique();
        });

        modelBuilder.Entity<FamilyMember>(entity =>
        {
            // Mot nguoi mot lan trong mot gia dinh. Khong co rang buoc nay thi "vao nhom hai
            // lan" tao ra hai dong voi hai vai tro khac nhau, va cau hoi "vai tro cua nguoi
            // nay la gi" khong con mot dap an.
            entity.HasIndex(m => new { m.FamilyID, m.UserID }).IsUnique();

            // Truy van nong nhat cua M0: "cac gia dinh cua nguoi dang dang nhap".
            entity.HasIndex(m => m.UserID);

            entity.Property(m => m.MemberRole).HasConversion<string>().HasMaxLength(16);

            // Cascade: thanh vien khong ton tai doc lap voi gia dinh.
            entity.HasOne(m => m.Family)
                .WithMany(f => f.Members)
                .HasForeignKey(m => m.FamilyID)
                .OnDelete(DeleteBehavior.Cascade);

            // Cascade theo nguoi dung: xoa tai khoan thi tu cach thanh vien di theo, giong
            // cach ScheduleItem lam. Restrict o day nghia la khong xoa noi mot tai khoan.
            entity.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserID)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FamilyInvite>(entity =>
        {
            // Tra cuu luc join chi co ma trong tay — unique index nay vua la rang buoc vua
            // la duong truy van.
            entity.HasIndex(i => i.Code).IsUnique();

            entity.HasOne(i => i.Family)
                .WithMany()
                .HasForeignKey(i => i.FamilyID)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Space>(entity =>
        {
            entity.HasIndex(s => s.SpaceUUID).IsUnique();

            // 1 family = 1 space. Partial-unique qua filter EF sinh tu Where-tuong-duong:
            // dung HasFilter truc tiep vi FamilyID la nullable va chi Family-type moi dat no.
            entity.HasIndex(s => s.FamilyID).IsUnique().HasFilter("family_id IS NOT NULL");

            entity.HasOne(s => s.Family)
                .WithMany()
                .HasForeignKey(s => s.FamilyID)
                .OnDelete(DeleteBehavior.Cascade);

            // FK toi security.users — Space.OwnerUserID phai tro toi mot tai khoan that,
            // giong cach FamilyMember/ScheduleItem lam voi UserID.
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(s => s.OwnerUserID)
                .OnDelete(DeleteBehavior.Restrict);

            // 1 personal space moi user — chan race trong EnsureMyPersonalSpaceAsync o tang DB,
            // khong chi o tang application.
            entity.HasIndex(s => s.OwnerUserID)
                .IsUnique()
                .HasFilter("space_type = 0")
                .HasDatabaseName("ix_spaces_owner_personal_unique");
        });

        modelBuilder.Entity<SpaceMember>(entity =>
        {
            entity.HasIndex(m => new { m.SpaceID, m.UserID }).IsUnique();
            entity.HasIndex(m => m.UserID);

            entity.HasOne(m => m.Space)
                .WithMany()
                .HasForeignKey(m => m.SpaceID)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserID)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasIndex(c => c.SpaceID);
            entity.HasIndex(c => new { c.SpaceID, c.Name }).IsUnique();
            entity.HasOne(c => c.Space).WithMany().HasForeignKey(c => c.SpaceID).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasIndex(t => t.SpaceID);
            entity.HasIndex(t => new { t.SpaceID, t.Name }).IsUnique();
            entity.HasOne(t => t.Space).WithMany().HasForeignKey(t => t.SpaceID).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Prompt>(entity =>
        {
            entity.HasIndex(p => p.SpaceID);
            entity.HasIndex(p => p.CategoryID);
            entity.HasIndex(p => new { p.SpaceID, p.IsFavorite }).HasFilter("is_deleted = false");
            entity.HasIndex(p => new { p.SpaceID, p.UpdatedDate }).HasFilter("is_deleted = false");
            // FTS: y het thiet ke trong db/init-postgres.sql, sinh bang raw SQL trong migration
            // (Task 5 Step 6) vi HasGeneratedTsVectorColumn khong khop cach dung to_tsvector
            // truc tiep tren 2 cot ma khong luu them cot moi.

            entity.HasOne(p => p.Space).WithMany().HasForeignKey(p => p.SpaceID).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryID).OnDelete(DeleteBehavior.SetNull);

            // Ruling (Low, decision, confirmed correct trong progress.md): Restrict la co y —
            // xoa mot user con prompt do ho tao ra phai that bai ro rang, khong am tham
            // mo con/xoa lan noi dung dang chia se trong mot space.
            entity.HasOne(p => p.Author).WithMany().HasForeignKey(p => p.AuthorUserID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PromptVariable>(entity =>
        {
            entity.HasIndex(v => v.PromptID);
            entity.HasIndex(v => new { v.PromptID, v.VarKey }).IsUnique();
            entity.HasOne(v => v.Prompt).WithMany(p => p.Variables).HasForeignKey(v => v.PromptID).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PromptTag>(entity =>
        {
            entity.HasKey(pt => new { pt.PromptID, pt.TagID });
            entity.HasOne(pt => pt.Prompt).WithMany(p => p.PromptTags).HasForeignKey(pt => pt.PromptID).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(pt => pt.Tag).WithMany().HasForeignKey(pt => pt.TagID).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(pt => pt.TagID);
        });
    }
}
